using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Watchdog.TM;

// V4.5.0: user levels with permission checkboxes and per-level sensor visibility.
public static class Release450Tests
{
 public static async Task Run()
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS 4.5.0 "+name);}
  var dir=new DirectoryInfo(AppContext.BaseDirectory);
  while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,"src","Watchdog.TM.Web","Program.cs")))dir=dir.Parent;
  if(dir==null)throw new Exception("Cannot locate src/Watchdog.TM.Web");
  var web=Path.Combine(dir.FullName,"src","Watchdog.TM.Web");

  // Every API route is either listed with a permission or deliberately Administrator-only.
  var adminOnly=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"POST license","POST restore","GET backup","GET users","POST users/level","POST users/level/{id:guid}/delete","POST users/user","POST users/user/{id:guid}/delete"};
  var routes=new List<string>();
  foreach(var file in Directory.GetFiles(web,"*.cs"))
   foreach(Match m in Regex.Matches(File.ReadAllText(file),@"api\.Map(Get|Post)\(""/([^""]+)"""))
    routes.Add(m.Groups[1].Value.ToUpperInvariant()+" "+m.Groups[2].Value);
  Check(routes.Count>=35,"found the API routes in the source ("+routes.Count+")");
  var unlisted=routes.Where(r=>!Access.ListedRoutes.Contains(r,StringComparer.OrdinalIgnoreCase)&&!adminOnly.Contains(r)).ToArray();
  Check(unlisted.Length==0,"every API route has a permission rule: "+string.Join(", ",unlisted));
  Check(routes.All(r=>Access.ListedRoutes.Contains(r,StringComparer.OrdinalIgnoreCase)||adminOnly.Contains(r)),"no route left open by accident");

  var sensorA=Guid.NewGuid();var sensorB=Guid.NewGuid();
  var admin=new Access.Principal(Access.AdminId,"Administrator",true,new HashSet<string>(),true,new HashSet<Guid>());
  var viewer=new Access.Principal("v","viewer",false,new HashSet<string>{Access.Trends},false,new HashSet<Guid>{sensorA});
  string R(string m,string p)=>Access.Route(m,p);
  Check(Access.Route("get","/api/trend/{id:guid}")=="GET trend/{id:guid}","route pattern normalised");
  Check(Access.Check(admin,R("POST","/api/license"),null)==null&&Access.Check(admin,R("POST","/api/users/user"),null)==null,"Administrator can do everything");
  Check(Access.Check(viewer,R("POST","/api/license"),null)!=null&&Access.Check(viewer,R("GET","/api/users"),null)!=null&&Access.Check(viewer,R("GET","/api/backup"),null)!=null,"users, license and backup are Administrator-only");
  Check(Access.Check(viewer,R("GET","/api/some-future-route"),null)!=null,"an unknown route is Administrator-only");
  Check(Access.Check(viewer,R("GET","/api/state"),null)==null&&Access.Check(viewer,R("POST","/api/password"),null)==null,"every user can view the dashboard and change their own password");
  Check(Access.Check(viewer,R("GET","/api/trend/{id:guid}"),sensorA)==null,"ticked permission on a visible sensor is allowed");
  Check(Access.Check(viewer,R("GET","/api/trend/{id:guid}"),sensorB)=="Your user level does not include this sensor.","a sensor outside the level is refused");
  Check(Access.Check(viewer,R("POST","/api/alarms/{id:guid}"),sensorA)!.Contains("Change alarm limits"),"unticked permission is refused with its name");
  Check(Access.Check(viewer,R("POST","/api/alarms/{id:guid}/acknowledge"),sensorA)!=null&&Access.Check(viewer,R("POST","/api/mode"),null)!=null&&Access.Check(viewer,R("POST","/api/controller"),null)!=null,"control actions need their own checkbox");

  // Accounts are stored in the configuration and resolved for each request.
  var path=Path.Combine(Environment.CurrentDirectory,"work","users-test-"+Guid.NewGuid());Directory.CreateDirectory(path);
  IConfiguration config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Storage:DataDirectory",path}}).Build();
  var s=new ServerState(config);
  var start=s.Clone();var p=new Controller{Name="F2A"};start.Controllers.Add(p);start.Sensors.Add(new Sensor{Id=sensorA,ControllerId=p.Id,Name="CL-01"});start.Sensors.Add(new Sensor{Id=sensorB,ControllerId=p.Id,Name="Pha-02"});
  start.Settings.PasswordHash=Security.Hash("admin-password-1");await s.Save(start,"test setup");
  async Task<int> Status(Task<IResult> call){var r=await call;return (r as IStatusCodeHttpResult)?.StatusCode??200;}
  Check(await Status(UserEndpoints.SaveLevel(s,new(s.Revision,Guid.Empty,"Pharmacy",[Access.Trends,Access.Acknowledge],false,[]))) ==400,"a limited level needs at least one sensor");
  Check(await Status(UserEndpoints.SaveLevel(s,new(s.Revision,Guid.Empty,"Pharmacy",["format-disk"],true,[]))) ==400,"unknown permissions are rejected");
  Check(await Status(UserEndpoints.SaveLevel(s,new(s.Revision,Guid.Empty,"Pharmacy",[Access.Acknowledge,Access.Trends],false,[sensorB])))==200,"level saved with ticked permissions and one sensor");
  var level=s.Engine.Config.Settings.UserLevels.Single();
  Check(level.Permissions.SequenceEqual([Access.Trends,Access.Acknowledge])&&!level.AllSensors&&level.Sensors.SequenceEqual([sensorB]),"level stores permissions in catalogue order and its sensors");
  Check(await Status(UserEndpoints.SaveLevel(s,new(s.Revision,Guid.Empty,"pharmacy",[],true,[])))==400,"level names are unique");
  Check(await Status(UserEndpoints.SaveUser(s,new(s.Revision,Guid.Empty,"Administrator",level.Id,true,"0123456789")))==400,"'Administrator' cannot be used as a user name");
  Check(await Status(UserEndpoints.SaveUser(s,new(s.Revision,Guid.Empty,"nurse",level.Id,true,"short")))==400,"short passwords are rejected");
  Check(await Status(UserEndpoints.SaveUser(s,new(s.Revision,Guid.Empty,"nurse",level.Id,true,null)))==400,"a new user needs a password");
  Check(await Status(UserEndpoints.SaveUser(s,new(s.Revision,Guid.Empty,"nurse",level.Id,true,"nurse-password-1")))==200,"user created");
  Check(await Status(UserEndpoints.SaveUser(s,new(s.Revision,Guid.Empty,"NURSE",level.Id,true,"nurse-password-1")))==400,"user names are unique");
  var nurse=s.Engine.Config.Settings.Users.Single();
  Check(Security.Verify("nurse-password-1",nurse.PasswordHash)&&!s.Store.Audits().Any(a=>a.Contains("nurse-password-1")),"password is hashed and never audited");
  ClaimsPrincipal Cookie(string uid)=>new(new ClaimsIdentity([new Claim(ClaimTypes.Name,"x"),new Claim("uid",uid)],"test"));
  var who=Access.Resolve(Cookie(nurse.Id.ToString()),s.Engine.Config);
  Check(!who.Admin&&who.Name=="nurse"&&who.Can(Access.Acknowledge)&&!who.Can(Access.AlarmLimits)&&who.Sees(sensorB)&&!who.Sees(sensorA),"signed-in user gets the level's permissions and sensors");
  Check(Access.Resolve(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,"Administrator")],"test")),s.Engine.Config).Admin,"cookies from V4.4.1 and earlier stay Administrator");
  Check(Access.HashFor(nurse.Id.ToString(),s.Engine.Config)==nurse.PasswordHash&&Access.HashFor(null,s.Engine.Config)==s.Engine.Config.Settings.PasswordHash,"session stamp follows each account's own password");
  Check(await Status(UserEndpoints.DeleteLevel(s,level.Id,s.Revision))==400,"a level in use cannot be deleted");
  Check(await Status(UserEndpoints.SaveUser(s,new(s.Revision,nurse.Id,"nurse",level.Id,false,null)))==200,"user disabled without changing the password");
  Check(Access.HashFor(nurse.Id.ToString(),s.Engine.Config)==null&&Access.Resolve(Cookie(nurse.Id.ToString()),s.Engine.Config).Permissions.Count==0,"a disabled user's session ends and gets no permissions");
  Check(Security.Verify("nurse-password-1",s.Engine.Config.Settings.Users.Single().PasswordHash),"blank password keeps the existing one");
  Check(await Status(UserEndpoints.DeleteUser(s,nurse.Id,s.Revision))==200&&await Status(UserEndpoints.DeleteLevel(s,level.Id,s.Revision))==200,"user and then level deleted");
  Check(s.Engine.Config.Settings.Users.Count==0&&s.Engine.Config.Settings.UserLevels.Count==0,"configuration is clean after deletion");
  Check(await Status(UserEndpoints.SaveLevel(s,new("stale",Guid.Empty,"Late",[],true,[])))==409,"stale revision is refused");

  // Browser: login asks for a user name, menu and controls follow the level.
  var root=Path.Combine(web,"wwwroot");var html=File.ReadAllText(Path.Combine(root,"index.html"));var js=File.ReadAllText(Path.Combine(root,"app.js"));var users=File.ReadAllText(Path.Combine(root,"users.js"));
  Check(html.Contains("id=\"username\"")&&js.Contains("username:setup?'':$('username').value"),"sign-in sends the user name");
  Check(html.Contains("href=\"#users\" data-page=\"users\"")&&html.Contains("users.js"),"Users and levels page is in the menu");
  Check(users.Contains("data-permission=")&&users.Contains("data-level-sensor=")&&users.Contains("api('users/level'"),"level editor has permission and sensor checkboxes");
  Check(js.Contains("pageAllowed(")&&js.Contains("applyAccess"),"pages and buttons are hidden for unticked permissions");
 }
}
