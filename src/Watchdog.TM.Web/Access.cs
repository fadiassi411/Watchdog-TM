using System.Security.Claims;
using Microsoft.AspNetCore.Routing;
using Watchdog.TM;

// V4.5.0 user levels. The built-in Administrator has every permission and is the only account that can manage
// users, levels, the license, backups and restores. Every API route is listed here; an unlisted route is
// Administrator-only, so a new endpoint is never opened to other users by accident.
public static class Access
{
    public const string AdminId="admin";
    public const string Trends="trends",Export="export",Audit="audit",Acknowledge="acknowledge",AlarmLimits="alarm-limits",TestRead="test-read",
        Sensors="sensors",Controllers="controllers",PlcHistorySetup="plc-history",Monitoring="monitoring",Email="email";
    public sealed record PermissionInfo(string Id,string Group,string Label,string Description);
    public static readonly PermissionInfo[] Catalog=[
        new(Trends,"Monitor","View trends and history","Trends page, saved readings and PLC combined history."),
        new(Export,"Monitor","Export to Excel / CSV","Download temperature reports."),
        new(Audit,"Monitor","View audit history","See configuration changes and who made them."),
        new(Acknowledge,"Control","Acknowledge alarms","Stop blinking and reminder emails for an active alarm."),
        new(AlarmLimits,"Control","Change alarm limits","High / Low Alarm, delay and hysteresis."),
        new(TestRead,"Control","Test read sensors","Read one temperature from the PLC on demand."),
        new(Sensors,"Control","Add, edit and delete sensors","Sensor registers, formats and recording interval."),
        new(Controllers,"Control","Add, edit and delete controllers","PLC connection settings."),
        new(PlcHistorySetup,"Control","PLC history setup","History layouts, header tests, downloads and time-zone corrections."),
        new(Monitoring,"Control","Start / stop live monitoring","Live or demonstration mode and computer sleep protection."),
        new(Email,"Control","Email and SMTP settings","Mail server, sensor recipients and test emails.")];
    public static bool Known(string permission)=>Catalog.Any(x=>x.Id==permission);

    // Required permission per route ("" = any signed-in user, null = Administrator only) and whether {id} is a sensor.
    static readonly Dictionary<string,(string? Permission,bool SensorId)> Routes=new(StringComparer.OrdinalIgnoreCase){
        ["GET state"]=("",false),["POST logout"]=("",false),["POST password"]=("",false),
        ["GET trend/{id:guid}"]=(Trends,true),["GET history/{id:guid}"]=(Trends,true),["GET plc-history"]=(Trends,false),["GET stored-history"]=(Trends,false),
        ["GET export/{id:guid}"]=(Export,true),["GET stored-history/export"]=(Export,false),
        ["GET audit"]=(Audit,false),
        ["POST alarms/{id:guid}/acknowledge"]=(Acknowledge,true),
        ["POST alarms/{id:guid}"]=(AlarmLimits,true),
        ["POST test/{id:guid}"]=(TestRead,true),
        ["POST sensor"]=(Sensors,false),["POST sensor/{id:guid}/delete"]=(Sensors,true),["POST recording/{id:guid}"]=(Sensors,true),
        ["POST controller"]=(Controllers,false),["POST controller/{id:guid}/delete"]=(Controllers,false),
        ["POST plc-history/{id:guid}/mapping"]=(PlcHistorySetup,false),["POST plc-history/{id:guid}/migrate-utc-offset"]=(PlcHistorySetup,false),
        ["GET plc-history/{id:guid}/standard-layout"]=(PlcHistorySetup,false),["POST plc-history/{id:guid}/download"]=(PlcHistorySetup,false),["POST plc-history/{id:guid}/test-header"]=(PlcHistorySetup,false),
        ["POST mode"]=(Monitoring,false),["POST power"]=(Monitoring,false),
        ["GET smtp"]=(Email,false),["POST smtp"]=(Email,false),["POST smtp/test"]=(Email,false),["POST smtp/sensor/{id:guid}"]=(Email,true),["POST smtp/sensor/{id:guid}/test"]=(Email,true),
    };

    public static IEnumerable<string> ListedRoutes=>Routes.Keys;
    public sealed record Principal(string Id,string Name,bool Admin,IReadOnlySet<string> Permissions,bool AllSensors,IReadOnlySet<Guid> Sensors)
    {
        public bool Can(string permission)=>Admin||Permissions.Contains(permission);
        public bool Sees(Guid sensor)=>Admin||AllSensors||Sensors.Contains(sensor);
    }
    static readonly Principal Nobody=new("","",false,new HashSet<string>(),false,new HashSet<Guid>());
    public static Principal Resolve(ClaimsPrincipal user,Configuration c)
    {
        var id=user.FindFirstValue("uid")??(user.Identity?.IsAuthenticated==true?AdminId:"");
        if(id==AdminId)return new(AdminId,"Administrator",true,Catalog.Select(x=>x.Id).ToHashSet(),true,new HashSet<Guid>());
        var account=c.Settings.Users.FirstOrDefault(x=>x.Id.ToString()==id&&x.Enabled);
        if(account is null)return Nobody;
        var level=c.Settings.UserLevels.FirstOrDefault(x=>x.Id==account.LevelId);
        if(level is null)return new(id,account.Name,false,new HashSet<string>(),false,new HashSet<Guid>());
        return new(id,account.Name,false,level.Permissions.Where(Known).ToHashSet(),level.AllSensors,level.Sensors.ToHashSet());
    }
    public static Principal Current(HttpContext ctx)=>ctx.Items["watchdog.principal"] as Principal??Resolve(ctx.User,ctx.RequestServices.GetRequiredService<ServerState>().Engine.Config);

    // Password hash for the account in a sign-in cookie; null when the account no longer exists or is disabled.
    public static string? HashFor(string? id,Configuration c)=>id is null or AdminId?c.Settings.PasswordHash:c.Settings.Users.FirstOrDefault(x=>x.Id.ToString()==id&&x.Enabled)?.PasswordHash;

    public static string Route(string method,string rawPattern)
    {
        var p=rawPattern.Trim('/');if(p.StartsWith("api/",StringComparison.OrdinalIgnoreCase))p=p[4..];
        return method.ToUpperInvariant()+" "+p;
    }
    // Returns null when allowed, otherwise the reason to show.
    public static string? Check(Principal who,string route,object? sensorRouteValue)
    {
        if(!Routes.TryGetValue(route,out var rule))return who.Admin?null:"Only the Administrator can do this.";
        if(rule.Permission is null)return who.Admin?null:"Only the Administrator can do this.";
        if(rule.Permission!=""&&!who.Can(rule.Permission))return "Your user level does not allow this. Ask the Administrator to enable \""+Catalog.First(x=>x.Id==rule.Permission).Label+"\".";
        if(rule.SensorId&&Guid.TryParse(sensorRouteValue?.ToString(),out var sensor)&&!who.Sees(sensor))return "Your user level does not include this sensor.";
        return null;
    }
    public static async ValueTask<object?> Filter(EndpointFilterInvocationContext context,EndpointFilterDelegate next)
    {
        var ctx=context.HttpContext;var who=Current(ctx);ctx.Items["watchdog.principal"]=who;
        var pattern=(ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText??ctx.Request.Path.Value??"";
        var reason=Check(who,Route(ctx.Request.Method,pattern),ctx.Request.RouteValues.GetValueOrDefault("id"));
        if(reason is not null)return Results.Json(new{error=reason},statusCode:403);
        Actor.Name.Value=who.Name;
        return await next(context);
    }
}

// Name written to the audit log for changes made during the current request.
public static class Actor
{
    public static readonly AsyncLocal<string?> Name=new();
    public static string Current=>string.IsNullOrEmpty(Name.Value)?"Administrator":Name.Value;
}
