using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Watchdog.TM;

var builder=WebApplication.CreateBuilder(new WebApplicationOptions { Args=args, ContentRootPath=AppContext.BaseDirectory });
// Same process exclusion as the desktop application: never compete for its serial ports.
using var mutex=new Mutex(true,builder.Configuration["Monitoring:MutexName"]??"Global\\WatchdogTMMonitoring",out bool first);
if(!first) throw new InvalidOperationException("Stop the Watchdog TM desktop application or other TM server before starting this server.");
if(args.Length==3 && args[0]=="--migrate-db")
{
    var destination=Path.GetFullPath(args[2]);
    if(File.Exists(destination)) {Console.WriteLine("Existing server database retained.");return;}
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    using var source=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=Path.GetFullPath(args[1]),Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());source.Open();
    using var target=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=destination,Pooling=false}.ToString());target.Open();source.BackupDatabase(target);
    using var check=target.CreateCommand();check.CommandText="PRAGMA integrity_check";if((string?)check.ExecuteScalar()!="ok")throw new IOException("Migration integrity check failed.");
    Console.WriteLine("Desktop database migrated without changing configuration or history.");return;
}
builder.Host.UseWindowsService(o=>o.ServiceName="Watchdog Temperature Monitoring Server");
if(string.IsNullOrEmpty(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://0.0.0.0:5081");
builder.Services.AddSingleton<ServerState>();
builder.Services.AddHostedService(sp=>sp.GetRequiredService<ServerState>());
builder.Services.AddHostedService<NotificationWorker>();
var data=builder.Configuration["Storage:DataDirectory"]??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Watchdog TM");
builder.Logging.AddProvider(new DailyFileLoggerProvider(Path.Combine(data,"Logs")));
var protection=builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data,"Keys"))).SetApplicationName("WatchdogTM.Web");
if(OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(true);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o=>{
    o.Cookie.Name="WatchdogTM.Session"; o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Strict;
    o.Cookie.SecurePolicy=CookieSecurePolicy.SameAsRequest;o.ExpireTimeSpan=TimeSpan.FromMinutes(30);o.SlidingExpiration=true;
    o.Events.OnRedirectToLogin=c=>{c.Response.StatusCode=401;return Task.CompletedTask;};
    o.Events.OnValidatePrincipal=c=>{var server=c.HttpContext.RequestServices.GetRequiredService<ServerState>();if(c.Principal?.FindFirstValue("stamp")!=Stamp(server.Engine.Config.Settings.PasswordHash))c.RejectPrincipal();return Task.CompletedTask;};
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o=>{o.HeaderName="X-CSRF-TOKEN";o.Cookie.Name="WatchdogTM.Csrf";o.Cookie.SameSite=SameSiteMode.Strict;});
builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("login",context=>RateLimitPartition.GetFixedWindowLimiter("login",_=>new FixedWindowRateLimiterOptions{PermitLimit=5,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
var app=builder.Build();
app.Use(async(ctx,next)=>{
    ctx.Response.Headers["X-Content-Type-Options"]="nosniff";ctx.Response.Headers["X-Frame-Options"]="DENY";
    ctx.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if(ctx.Request.Path.StartsWithSegments("/api"))ctx.Response.Headers.CacheControl="no-store";
    try {await next();} catch(AntiforgeryValidationException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{error="Session validation failed. Reload this page."});}
    catch(Exception ex){app.Logger.LogError(ex,"Request failed at {Path}",ctx.Request.Path);ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{error=ex is IOException?"Communication or file operation failed. Check controller connectivity and server logs.":ex.Message});}
});
app.UseDefaultFiles();app.UseStaticFiles();app.UseRouting();app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();
app.MapGet("/health",()=>Results.Ok(new{status="running"}));
app.MapGet("/api/session",(HttpContext ctx,IAntiforgery csrf,ServerState s)=>Results.Ok(new{authenticated=ctx.User.Identity?.IsAuthenticated==true,setup=s.Engine.Config.Settings.PasswordHash.Length==0,csrf=csrf.GetAndStoreTokens(ctx).RequestToken}));
app.MapPost("/api/login",async(HttpContext ctx,IAntiforgery csrf,ServerState s,Login input)=>{
    await csrf.ValidateRequestAsync(ctx);
    if(input.Password.Length>256||!Security.Verify(input.Password,s.Engine.Config.Settings.PasswordHash))return Results.Json(new{error="Incorrect administrator password."},statusCode:401);
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,"Administrator"),new Claim("stamp",Stamp(s.Engine.Config.Settings.PasswordHash))},CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok();
}).RequireRateLimiting("login");
app.MapPost("/api/setup",async(HttpContext ctx,IAntiforgery csrf,ServerState s,Login input)=>{
    await csrf.ValidateRequestAsync(ctx);if(ctx.Connection.RemoteIpAddress is not {} ip||!IPAddress.IsLoopback(ip))return Results.StatusCode(403);
    await s.Gate.WaitAsync();try{if(s.Engine.Config.Settings.PasswordHash.Length!=0)return Results.Conflict();if(input.Password.Length>256)return Results.BadRequest();var c=s.Clone();c.Settings.PasswordHash=Security.Hash(input.Password);await s.Save(c,"Initial web administrator created");return Results.Ok();}finally{s.Gate.Release();}
}).RequireRateLimiting("login");
var api=app.MapGroup("/api").RequireAuthorization();
api.AddEndpointFilter(async(context,next)=>{var ctx=context.HttpContext;if(ctx.Request.Method!="GET")await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx);return await next(context);});
api.MapSmtp();
api.MapPost("/logout",async(HttpContext ctx)=>{await ctx.SignOutAsync();return Results.Ok();});
api.MapGet("/state",async (ServerState s)=>{ await s.Gate.WaitAsync(); try { return Results.Ok(new{
    revision=s.Revision,simulation=s.Engine.Config.Settings.Simulation,site=s.Engine.Config.Settings.Site,
    license=Security.LicenseStatus(s.Engine.Config),installationId=Security.InstallationId,
    controllers=s.Engine.Config.Controllers.ToArray(),
    sensors=s.Engine.Config.Sensors.Select(x=>new{x.Id,x.ControllerId,x.Name,x.Location,x.Enabled,x.Retired,x.TemperatureOffset,x.TemperatureFunction,x.Format,x.Order,x.Multiplier,x.Offset,x.Decimals,x.SampleSeconds,x.SoftwareHighLimit,x.SoftwareLowLimit}).ToArray(),
    readings=s.Engine.Config.Sensors.Select(x=>{var reading=s.Engine.Display(x);return new{id=x.Id,reading,alarm=SoftwareAlarms.Evaluate(x,reading)};}).ToArray(),states=s.Engine.States.ToDictionary(x=>x.Key,x=>new{x.Value.Status,x.Value.LastSuccess,x.Value.Error}),
    fault=s.Store.Fault+" "+s.Engine.OperationalFault
}); } finally { s.Gate.Release(); } });
api.MapPost("/controller",async(ServerState s,ControllerEdit edit)=>{
    await s.Gate.WaitAsync();try{if(edit.Revision!=s.Revision)return Results.Conflict(new{error="Settings changed in another browser. Reload and retry."});var c=s.Clone();var existing=c.Controllers.FindIndex(x=>x.Id==edit.Controller.Id);if(existing<0){edit.Controller.Id=Guid.NewGuid();c.Controllers.Add(edit.Controller);}else c.Controllers[existing]=edit.Controller;await s.Save(c,"Controller setup updated; live monitoring applied automatically");return Results.Ok();}finally{s.Gate.Release();}
});
api.MapPost("/sensor",async(ServerState s,SensorEdit edit)=>{
    await s.Gate.WaitAsync();try{if(edit.Revision!=s.Revision)return Results.Conflict(new{error="Settings changed in another browser. Reload and retry."});var c=s.Clone();var old=c.Sensors.FirstOrDefault(x=>x.Id==edit.Sensor.Id);var sensor=old??new Sensor();
    sensor.ControllerId=edit.Sensor.ControllerId;sensor.Name=edit.Sensor.Name;sensor.Location=edit.Sensor.Location;sensor.Enabled=edit.Sensor.Enabled;sensor.Retired=edit.Sensor.Retired;sensor.TemperatureOffset=edit.Sensor.TemperatureOffset;sensor.TemperatureFunction=edit.Sensor.TemperatureFunction;sensor.Format=edit.Sensor.Format;sensor.Order=edit.Sensor.Order;sensor.Multiplier=edit.Sensor.Multiplier;sensor.Offset=edit.Sensor.Offset;sensor.Decimals=edit.Sensor.Decimals;sensor.SampleSeconds=edit.Sensor.SampleSeconds;
    if(old==null)c.Sensors.Add(sensor);


    await s.Save(c,"Temperature sensor setup updated; live monitoring applied automatically");return Results.Ok();}finally{s.Gate.Release();}
});
api.MapPost("/alarms/{id:guid}",async(Guid id,ServerState s,AlarmLimits input)=>{
    await s.Gate.WaitAsync();try{
        if(input.Revision!=s.Revision)return Results.Conflict(new{error="Settings changed in another browser. Reload and retry."});
        var c=s.Clone();var sensor=c.Sensors.Single(x=>x.Id==id);
        sensor.SoftwareHighLimit=input.High;sensor.SoftwareLowLimit=input.Low;
        await s.Save(c,$"Software alarm limits updated for {sensor.Id}: high={input.High}, low={input.Low}. No PLC write.");
        return Results.Ok();
    }finally{s.Gate.Release();}
});
api.MapPost("/mode",async(ServerState s,Mode input)=>{
    await s.Gate.WaitAsync();try{var c=s.Clone();c.Settings.Simulation=input.Simulation;if(!input.Simulation){Rules.Validate(c);Security.Capacity(c);if(c.Controllers.Any(x=>x.Enabled&&x.Protocol==Protocol.Simulation)||c.Sensors.Any(x=>x.Enabled&&!x.Retired&&!x.Mapped))return Results.BadRequest(new{error="Enable a real controller and set each active sensor temperature register."});if(!c.Controllers.Any(x=>x.Enabled)||!c.Sensors.Any(x=>x.Enabled&&!x.Retired))return Results.BadRequest(new{error="Enable a controller and sensor first."});c.Settings.CommissionedSensors=c.Sensors.Where(x=>!x.Retired&&x.Enabled).Select(x=>x.Id).ToList();}await s.Save(c,input.Simulation?"Switched to demonstration":"Started temperature-only live monitoring");return Results.Ok();}finally{s.Gate.Release();}
});
api.MapPost("/test/{id:guid}",async(Guid id,ServerState s,CancellationToken ct)=>{await s.Gate.WaitAsync(ct);try{var sensor=s.Engine.Config.Sensors.Single(x=>x.Id==id);var controller=s.Engine.Config.Controllers.Single(x=>x.Id==sensor.ControllerId);var value=await s.Engine.Transport.ReadTemperature(controller,sensor,ct);return Results.Ok(new{value,at=DateTimeOffset.UtcNow,address=sensor.TemperatureOffset});}finally{s.Gate.Release();}});
api.MapGet("/trend/{id:guid}",(Guid id,DateTimeOffset from,DateTimeOffset to,int seconds,ServerState s,HttpContext ctx)=>{
    if(!s.Engine.Config.Sensors.Any(x=>x.Id==id))return Results.NotFound();
    try{return Results.Ok(Trends.Read(s.Store,id,from,to,seconds,ctx.RequestAborted));}
    catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}
});
api.MapPost("/recording/{id:guid}",async(Guid id,RecordingInterval input,ServerState s)=>{
    await s.Gate.WaitAsync();try{
        if(input.Revision!=s.Revision)return Results.Conflict(new{error="Settings changed. Reload this page and retry."});
        var c=s.Clone();var sensor=c.Sensors.FirstOrDefault(x=>x.Id==id);if(sensor==null)return Results.NotFound();
        sensor.SampleSeconds=input.Seconds;await s.Save(c,"Trend recording interval updated; live monitoring continues");return Results.Ok();
    }finally{s.Gate.Release();}
});
api.MapGet("/history/{id:guid}",(Guid id,DateTimeOffset from,DateTimeOffset to,ServerState s)=>{if(to<from||to-from>TimeSpan.FromDays(31))return Results.BadRequest(new{error="Select a range of up to 31 days."});return Results.Ok(s.Store.Samples(id,from,to));});
api.MapGet("/export/{id:guid}",(Guid id,DateTimeOffset from,DateTimeOffset to,ServerState s,HttpContext ctx)=>{if(to<from||to-from>TimeSpan.FromDays(31))return Results.BadRequest();var sensor=s.Engine.Config.Sensors.Single(x=>x.Id==id);var path=Path.Combine(s.DirectoryPath,Guid.NewGuid()+".xlsx");try{Exporter.Export(s.Store,sensor,s.Engine.Config.Controllers.Single(x=>x.Id==sensor.ControllerId),from,to,path,ctx.RequestAborted);return Results.File(File.ReadAllBytes(path),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","Watchdog-temperatures.xlsx");}finally{if(File.Exists(path))File.Delete(path);}});
api.MapPost("/license",async(ServerState s,LicenseInput input)=>{await s.Gate.WaitAsync();try{Security.License(input.Json);var c=s.Clone();c.Settings.LicenseJson=input.Json;Security.Capacity(c);await s.Save(c,"TM license imported");return Results.Ok();}finally{s.Gate.Release();}});
api.MapPost("/password",async(ServerState s,PasswordInput input,HttpContext ctx)=>{await s.Gate.WaitAsync();try{if(!Security.Verify(input.Current,s.Engine.Config.Settings.PasswordHash))return Results.BadRequest(new{error="Current password is incorrect."});if(input.Password.Length>256)return Results.BadRequest();var c=s.Clone();c.Settings.PasswordHash=Security.Hash(input.Password);await s.Save(c,"Administrator password changed");await ctx.SignOutAsync();return Results.Ok();}finally{s.Gate.Release();}});
api.MapGet("/backup",(ServerState s)=>{var path=Path.Combine(s.DirectoryPath,"download-"+Guid.NewGuid()+".db");try{s.Store.Backup(path);return Results.File(File.ReadAllBytes(path),"application/octet-stream","WatchdogTM-backup.db");}finally{if(File.Exists(path))File.Delete(path);}});
api.MapPost("/restore",async(HttpContext ctx,ServerState s)=>{var path=Path.Combine(s.DirectoryPath,"restore-"+Guid.NewGuid()+".db");await s.Gate.WaitAsync();try{if(ctx.Request.ContentLength is null or > 100_000_000)return Results.BadRequest(new{error="Select a backup up to 100 MB."});await using(var output=File.Create(path))await ctx.Request.Body.CopyToAsync(output);var candidate=s.Store.InspectBackup(path);Rules.Validate(candidate);candidate.Settings.LicenseJson=s.Engine.Config.Settings.LicenseJson;candidate.Settings.PasswordHash=s.Engine.Config.Settings.PasswordHash;candidate.Settings.Simulation=true;candidate.Settings.CommissionedSensors.Clear();s.Store.Backup(Path.Combine(s.DirectoryPath,"before-restore-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+".db"));await s.Engine.Stop();try{s.Store.Restore(path);await s.Save(candidate,"Backup restored; demonstration mode until live monitoring restarted");}catch{await s.Engine.Replace(s.Engine.Config);throw;}return Results.Ok();}finally{s.Gate.Release();if(File.Exists(path))File.Delete(path);}});
api.MapGet("/audit",(ServerState s)=>s.Store.Audits());
app.Run();
static string Stamp(string hash)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hash)));
record Login(string Password);
record ControllerEdit(string Revision,Controller Controller);
record SensorEdit(string Revision,Sensor Sensor);
record Mode(bool Simulation);
record LicenseInput(string Json);
record PasswordInput(string Current,string Password);
record AlarmLimits(string Revision,double? High,double? Low);




record RecordingInterval(string Revision,int Seconds);
