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
builder.Services.AddSingleton<PlcHistoryWorker>();
builder.Services.AddHostedService(sp=>sp.GetRequiredService<PlcHistoryWorker>());
var data=builder.Configuration["Storage:DataDirectory"]??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Watchdog TM");
builder.Logging.AddProvider(new DailyFileLoggerProvider(Path.Combine(data,"Logs")));
var protection=builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data,"Keys"))).SetApplicationName("WatchdogTM.Web");
if(OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(true);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o=>{
    o.Cookie.Name="WatchdogTM.Session"; o.Cookie.HttpOnly=true;o.Cookie.SameSite=SameSiteMode.Strict;
    o.Cookie.SecurePolicy=CookieSecurePolicy.SameAsRequest;o.ExpireTimeSpan=TimeSpan.FromDays(30);o.SlidingExpiration=true;
    o.Events.OnRedirectToLogin=c=>{c.Response.StatusCode=401;return Task.CompletedTask;};
    // V4.5.0: each cookie is tied to its own account; a password change, disable or delete ends that sign-in.
    o.Events.OnValidatePrincipal=c=>{var server=c.HttpContext.RequestServices.GetRequiredService<ServerState>();var hash=Access.HashFor(c.Principal?.FindFirstValue("uid"),server.Engine.Config);if(hash is null||c.Principal?.FindFirstValue("stamp")!=Stamp(hash))c.RejectPrincipal();return Task.CompletedTask;};
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o=>{o.HeaderName="X-CSRF-TOKEN";o.Cookie.Name="WatchdogTM.Csrf";o.Cookie.SameSite=SameSiteMode.Strict;});
builder.Services.AddRateLimiter(o=>{o.RejectionStatusCode=429;o.AddPolicy("login",context=>RateLimitPartition.GetFixedWindowLimiter("login:"+(context.Connection.RemoteIpAddress?.ToString()??"unknown"),_=>new FixedWindowRateLimiterOptions{PermitLimit=5,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));});
var app=builder.Build();
app.Use(async(ctx,next)=>{
    ctx.Response.Headers["X-Content-Type-Options"]="nosniff";ctx.Response.Headers["X-Frame-Options"]="DENY";
    ctx.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if(ctx.Request.Path.StartsWithSegments("/api"))ctx.Response.Headers.CacheControl="no-store";
    try {await next();}
    catch(OperationCanceledException) when(ctx.RequestAborted.IsCancellationRequested){}
    catch(AntiforgeryValidationException) when(!ctx.Response.HasStarted){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{error="Session validation failed. Reload this page."});}
    catch(Exception ex) when(!ctx.Response.HasStarted){
        app.Logger.LogError(ex,"Request failed at {Path}",ctx.Request.Path);
        // Validation messages are written for operators; internal errors are logged, not shown.
        var known=ex.GetType()==typeof(Exception)||ex is ArgumentException or InvalidDataException or FormatException or System.Text.Json.JsonException or BadHttpRequestException;
        ctx.Response.StatusCode=ex is IOException||known?400:500;
        await ctx.Response.WriteAsJsonAsync(new{error=ex is IOException?"Communication or file operation failed. Check controller connectivity and server logs.":known?ex.Message:"Unexpected server error. Details were written to the server log."});
    }
});
app.UseDefaultFiles();app.UseStaticFiles();app.UseRouting();app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();
app.MapGet("/health",()=>Results.Ok(new{status="running"}));
app.MapGet("/api/session",(HttpContext ctx,IAntiforgery csrf,ServerState s)=>{var signedIn=ctx.User.Identity?.IsAuthenticated==true;var who=signedIn?Access.Resolve(ctx.User,s.Engine.Config):null;
    return Results.Ok(new{authenticated=signedIn,setup=s.Engine.Config.Settings.PasswordHash.Length==0,csrf=csrf.GetAndStoreTokens(ctx).RequestToken,
    user=who is null?null:new{name=who.Name,admin=who.Admin,permissions=who.Permissions.ToArray(),allSensors=who.AllSensors}});});
app.MapPost("/api/login",async(HttpContext ctx,IAntiforgery csrf,ServerState s,Login input)=>{
    await csrf.ValidateRequestAsync(ctx);
    // V4.5.0: a blank user name (or "Administrator") signs in as the built-in administrator, as before.
    // V4.5.1: also "admin", or the "Sign in as Administrator" box, which ignores whatever the browser filled into the user name.
    var config=s.Engine.Config;var username=(input.Username??"").Trim();var admin=input.AsAdministrator||Access.IsAdministratorName(username);
    var account=admin?null:config.Settings.Users.FirstOrDefault(x=>x.Enabled&&x.Name.Equals(username,StringComparison.OrdinalIgnoreCase));
    var hash=admin?config.Settings.PasswordHash:account?.PasswordHash??"";
    var valid=input.Password.Length<=256&&Security.Verify(input.Password,hash.Length>0?hash:DummyHash.Value.Value)&&hash.Length>0;
    // A user-name sign-in with the Administrator password gets a hint instead of a bare refusal (the person already knows that password).
    if(!valid&&!admin&&input.Password.Length<=256&&config.Settings.PasswordHash.Length>0&&Security.Verify(input.Password,config.Settings.PasswordHash))return Results.Json(new{error=Access.AdministratorPasswordHint},statusCode:401);
    if(!valid)return Results.Json(new{error=admin?"Incorrect administrator password.":"Incorrect user name or password."},statusCode:401);
    await ctx.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Name,admin?"Administrator":account!.Name),new Claim("uid",admin?Access.AdminId:account!.Id.ToString()),new Claim("stamp",Stamp(hash))},CookieAuthenticationDefaults.AuthenticationScheme)),new AuthenticationProperties{IsPersistent=true,AllowRefresh=true});
    Actor.Name.Value=admin?"Administrator":account!.Name;s.Store.Audit(Actor.Current,"Signed in to the web server");
    return Results.Ok();
}).RequireRateLimiting("login");
app.MapPost("/api/setup",async(HttpContext ctx,IAntiforgery csrf,ServerState s,Login input)=>{
    await csrf.ValidateRequestAsync(ctx);if(ctx.Connection.RemoteIpAddress is not {} ip||!IPAddress.IsLoopback(ip))return Results.StatusCode(403);
    await s.Gate.WaitAsync();try{if(s.Engine.Config.Settings.PasswordHash.Length!=0)return Results.Conflict();if(input.Password.Length>256)return Results.BadRequest();var c=s.Clone();c.Settings.PasswordHash=Security.Hash(input.Password);await s.Save(c,"Initial web administrator created");return Results.Ok();}finally{s.Gate.Release();}
}).RequireRateLimiting("login");
var api=app.MapGroup("/api").RequireAuthorization();
api.AddEndpointFilter(async(context,next)=>{var ctx=context.HttpContext;if(ctx.Request.Method!="GET")await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx);return await next(context);});
api.AddEndpointFilter(Access.Filter);
api.MapUsers();
api.MapDashboard();
api.MapSmtp();
api.MapPlcHistory();
api.MapDeletion();
api.MapPost("/logout",async(HttpContext ctx)=>{await ctx.SignOutAsync();return Results.Ok();});
api.MapGet("/state",async (ServerState s,HttpContext ctx)=>{ await s.Gate.WaitAsync(); try { var who=Access.Current(ctx);var visible=DashboardLayout.Ordered(s.Engine.Config).Where(x=>who.Sees(x.Id)).ToArray(); return Results.Ok(new{
    revision=s.Revision,simulation=s.Engine.Config.Settings.Simulation,site=s.Engine.Config.Settings.Site,keepComputerAwake=s.Engine.Config.Settings.KeepComputerAwake,awakeStatus=s.PowerAvailability.Status,
    license=Security.LicenseStatus(s.Engine.Config),installationId=Security.InstallationId,
    controllers=s.Engine.Config.Controllers.Where(x=>who.AllSensors||who.Can(Access.Controllers)||visible.Any(v=>v.ControllerId==x.Id)).ToArray(),
    sensors=visible.Select(x=>new{x.Id,x.ControllerId,x.Name,x.Location,x.Enabled,x.Retired,x.TemperatureOffset,x.TemperatureFunction,x.Format,x.Order,x.Multiplier,x.Offset,x.Decimals,x.SampleSeconds,x.SoftwareHighLimit,x.SoftwareLowLimit,x.AlarmDelaySeconds,x.AlarmHysteresis}).ToArray(),
    readings=visible.Select(x=>{var reading=s.Engine.Display(x);var status=s.Notifications.Alarms.Current(x,reading);return new{id=x.Id,reading,alarm=status.Level,alarmStatus=status};}).ToArray(),states=s.Engine.States.ToDictionary(x=>x.Key,x=>new{x.Value.Status,x.Value.LastSuccess,x.Value.Error}),
    fault=s.Store.Fault+" "+s.Engine.OperationalFault+" "+s.MailFault
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
        var c=s.Clone();var sensor=c.Sensors.FirstOrDefault(x=>x.Id==id);if(sensor is null)return Results.NotFound();
        sensor.SoftwareHighLimit=input.High;sensor.SoftwareLowLimit=input.Low;
        sensor.AlarmDelaySeconds=input.DelaySeconds;sensor.AlarmHysteresis=input.Hysteresis;
        await s.Save(c,$"Software alarms updated for {sensor.Id}: high={input.High}, low={input.Low}, delay={input.DelaySeconds}s, hysteresis={input.Hysteresis}°C. No PLC write.");
        return Results.Ok();
    }finally{s.Gate.Release();}
});
api.MapPost("/alarms/{id:guid}/acknowledge",async(Guid id,ServerState s,AlarmAcknowledgement input)=>{
    await s.Gate.WaitAsync();try{
        var sensor=s.Engine.Config.Sensors.FirstOrDefault(x=>x.Id==id);
        if(sensor is null)return Results.NotFound();
        var status=s.Notifications.Alarms.Current(sensor,s.Engine.Display(sensor));
        if(status.Level is not ("High" or "Low") || !s.Notifications.Alarms.Acknowledge(id,input.Episode))
            return Results.Conflict(new{error="The alarm changed or is unavailable. Refresh and retry."});
        return Results.Ok();
    }finally{s.Gate.Release();}
});
api.MapPost("/mode",async(ServerState s,Mode input)=>{
    await s.Gate.WaitAsync();try{var c=s.Clone();c.Settings.Simulation=input.Simulation;if(!input.Simulation){Rules.Validate(c);Security.Capacity(c);if(c.Controllers.Any(x=>x.Enabled&&x.Protocol==Protocol.Simulation)||c.Sensors.Any(x=>x.Enabled&&!x.Retired&&!x.Mapped))return Results.BadRequest(new{error="Enable a real controller and set each active sensor temperature register."});if(!c.Controllers.Any(x=>x.Enabled)||!c.Sensors.Any(x=>x.Enabled&&!x.Retired))return Results.BadRequest(new{error="Enable a controller and sensor first."});c.Settings.CommissionedSensors=c.Sensors.Where(x=>!x.Retired&&x.Enabled).Select(x=>x.Id).ToList();}await s.Save(c,input.Simulation?"Switched to demonstration":"Started temperature-only live monitoring");return Results.Ok();}finally{s.Gate.Release();}
});
api.MapPost("/power",async(ServerState s,PowerInput input)=>{
    await s.Gate.WaitAsync();try{if(input.Revision!=s.Revision)return Results.Conflict(new{error="Settings changed in another browser. Reload and retry."});var c=s.Clone();c.Settings.KeepComputerAwake=input.KeepComputerAwake;await s.Save(c,input.KeepComputerAwake?"Enabled Windows awake request for live monitoring":"Disabled Windows awake request");return Results.Ok(new{status=s.PowerAvailability.Status});}finally{s.Gate.Release();}
});
api.MapPost("/test/{id:guid}",async(Guid id,ServerState s,CancellationToken ct)=>{
    // Read the device without holding the configuration lock: a slow controller must not freeze the dashboard.
    var config=s.Engine.Config;var sensor=config.Sensors.FirstOrDefault(x=>x.Id==id);var controller=sensor is null?null:config.Controllers.FirstOrDefault(x=>x.Id==sensor.ControllerId);
    if(sensor is null||controller is null)return Results.NotFound();
    var value=await s.Engine.Transport.ReadTemperature(controller,sensor,ct);return Results.Ok(new{value,at=DateTimeOffset.UtcNow,address=sensor.TemperatureOffset});
});
api.MapGet("/trend/{id:guid}",(Guid id,DateTimeOffset from,DateTimeOffset to,int seconds,ServerState s,HttpContext ctx)=>{
    if(!s.Engine.Config.Sensors.Any(x=>x.Id==id))return Results.NotFound();
    try{return Results.Ok(Trends.Read(s.Store,id,from,to,seconds,ctx.RequestAborted));}
    catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}
});
api.MapPost("/recording/{id:guid}",async(Guid id,RecordingInterval input,ServerState s)=>{
    await s.Gate.WaitAsync();try{
        if(input.Revision!=s.Revision)return Results.Conflict(new{error="Settings changed. Reload this page and retry."});
        var c=s.Clone();var sensor=c.Sensors.FirstOrDefault(x=>x.Id==id);if(sensor==null)return Results.NotFound();
        if(c.Controllers.Any(controller=>controller.Id==sensor.ControllerId&&controller.History.Enabled&&controller.History.Channels.Any(channel=>channel.SensorId==sensor.Id)))return Results.BadRequest(new{error="This sensor uses PLC history at 600 seconds. Configure sampling in the verified PLC program."});sensor.SampleSeconds=input.Seconds;await s.Save(c,"Trend recording interval updated; live monitoring continues");return Results.Ok();
    }finally{s.Gate.Release();}
});
api.MapGet("/history/{id:guid}",(Guid id,DateTimeOffset from,DateTimeOffset to,ServerState s)=>{if(to<from||to-from>TimeSpan.FromDays(31))return Results.BadRequest(new{error="Select a range of up to 31 days."});return Results.Ok(s.Store.Samples(id,from,to));});
api.MapGet("/export/{id:guid}",(Guid id,DateTimeOffset from,DateTimeOffset to,ServerState s,HttpContext ctx)=>{if(to<from||to-from>TimeSpan.FromDays(31))return Results.BadRequest(new{error="Select a range of up to 31 days."});var config=s.Engine.Config;var sensor=config.Sensors.FirstOrDefault(x=>x.Id==id);var controller=sensor is null?null:config.Controllers.FirstOrDefault(x=>x.Id==sensor.ControllerId);if(sensor is null||controller is null)return Results.NotFound();var path=Path.Combine(s.DirectoryPath,"export-"+Guid.NewGuid()+".xlsx");try{Exporter.Export(s.Store,sensor,controller,from,to,path,ctx.RequestAborted);return Results.File(TemporaryFiles.OpenAndDelete(path),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","Watchdog-temperatures.xlsx");}catch{TemporaryFiles.Delete(path);throw;}});
api.MapPost("/license",async(ServerState s,LicenseInput input)=>{await s.Gate.WaitAsync();try{Security.License(input.Json);var c=s.Clone();c.Settings.LicenseJson=input.Json;Security.Capacity(c);await s.Save(c,"TM license imported");return Results.Ok();}finally{s.Gate.Release();}});
api.MapPost("/password",async(ServerState s,PasswordInput input,HttpContext ctx)=>{await s.Gate.WaitAsync();try{var who=Access.Current(ctx);if(!who.Admin){var mine=s.Engine.Config.Settings.Users.FirstOrDefault(x=>x.Id.ToString()==who.Id);if(mine is null||!Security.Verify(input.Current,mine.PasswordHash))return Results.BadRequest(new{error="Current password is incorrect."});if(input.Password.Length>256)return Results.BadRequest();var u=s.Clone();u.Settings.Users.First(x=>x.Id==mine.Id).PasswordHash=Security.Hash(input.Password);await s.Save(u,$"Password changed by user '{mine.Name}'");await ctx.SignOutAsync();return Results.Ok();}if(!Security.Verify(input.Current,s.Engine.Config.Settings.PasswordHash))return Results.BadRequest(new{error="Current password is incorrect."});if(input.Password.Length>256)return Results.BadRequest();var c=s.Clone();c.Settings.PasswordHash=Security.Hash(input.Password);await s.Save(c,"Administrator password changed");await ctx.SignOutAsync();return Results.Ok();}finally{s.Gate.Release();}});
// Streamed from a temporary file (deleted when the download closes) so large databases do not load into memory.
api.MapGet("/backup",(ServerState s)=>{var path=Path.Combine(s.DirectoryPath,"download-"+Guid.NewGuid()+".db");try{s.Store.Backup(path);return Results.File(TemporaryFiles.OpenAndDelete(path),"application/octet-stream","WatchdogTM-backup.db");}catch{TemporaryFiles.Delete(path);throw;}});
api.MapPost("/restore",async(HttpContext ctx,ServerState s)=>{var path=Path.Combine(s.DirectoryPath,"restore-"+Guid.NewGuid()+".db");
    // Kestrel's default 30 MB request limit would reject normal multi-year backups.
    var limit=ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();if(limit is {IsReadOnly:false})limit.MaxRequestBodySize=ServerState.MaximumRestoreBytes;
    await s.Gate.WaitAsync();try{if(ctx.Request.ContentLength is null or 0 or > ServerState.MaximumRestoreBytes)return Results.BadRequest(new{error="Select a Watchdog TM database backup up to 8 GB."});await using(var output=File.Create(path))await ctx.Request.Body.CopyToAsync(output,ctx.RequestAborted);var candidate=s.Store.InspectBackup(path);Rules.Validate(candidate);candidate.Settings.LicenseJson=s.Engine.Config.Settings.LicenseJson;candidate.Settings.PasswordHash=s.Engine.Config.Settings.PasswordHash;candidate.Settings.Users=s.Engine.Config.Settings.Users;candidate.Settings.UserLevels=s.Engine.Config.Settings.UserLevels;candidate.Settings.Simulation=true;candidate.Settings.CommissionedSensors.Clear();s.Store.Backup(Path.Combine(s.DirectoryPath,"before-restore-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+".db"));s.PruneSafetyBackups();await s.Engine.Stop();try{s.Store.Restore(path);s.Notifications.Ensure();s.Notifications.Alarms.ResetAll();s.Notifications.Reset();await s.Save(candidate,"Backup restored; demonstration mode until live monitoring restarted");}catch{await s.Engine.Replace(s.Engine.Config);throw;}return Results.Ok();}finally{s.Gate.Release();if(File.Exists(path))File.Delete(path);}});
api.MapGet("/audit",(ServerState s)=>s.Store.Audits());
app.Run();
static string Stamp(string hash)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hash)));
record Login(string Password,string? Username=null,bool AsAdministrator=false);
// Verifying against a fixed hash for unknown names keeps sign-in time the same whether or not the user exists.
static class DummyHash{public static readonly Lazy<string> Value=new(()=>Security.Hash(Guid.NewGuid().ToString()));}
record ControllerEdit(string Revision,Controller Controller);
record SensorEdit(string Revision,Sensor Sensor);
record Mode(bool Simulation);
record PowerInput(string Revision,bool KeepComputerAwake);
record LicenseInput(string Json);
record PasswordInput(string Current,string Password);
record AlarmLimits(string Revision,double? High,double? Low,int DelaySeconds=0,double Hysteresis=0);
record AlarmAcknowledgement(string Episode);
record RecordingInterval(string Revision,int Seconds);
