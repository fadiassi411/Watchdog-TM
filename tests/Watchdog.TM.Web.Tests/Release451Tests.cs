using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Watchdog.TM;

// V4.5.1: signing in as Administrator must not depend on what the browser filled into the user name box.
public static class Release451Tests
{
 public static async Task Run()
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS 4.5.1 "+name);}
  foreach(var n in new[]{"",null,"  ","Administrator","administrator","admin","ADMIN"," admin "})Check(Access.IsAdministratorName(n),$"'{n}' means the Administrator");
  foreach(var n in new[]{"Jawad","admin1","adminstrator"})Check(!Access.IsAdministratorName(n),$"'{n}' is a normal user name");
  var path=Path.Combine(Environment.CurrentDirectory,"work","users451-"+Guid.NewGuid());Directory.CreateDirectory(path);
  var s=new ServerState(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Storage:DataDirectory",path}}).Build());
  await UserEndpoints.SaveLevel(s,new(s.Revision,Guid.Empty,"Operator",[],true,[]));var level=s.Engine.Config.Settings.UserLevels.Single().Id;
  var r=await UserEndpoints.SaveUser(s,new(s.Revision,Guid.Empty,"admin",level,true,"0123456789ab"));
  Check((r as IStatusCodeHttpResult)?.StatusCode==400&&s.Engine.Config.Settings.Users.Count==0,"'admin' cannot be used as a user name");
  var dir=new DirectoryInfo(AppContext.BaseDirectory);
  while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,"src","Watchdog.TM.Web","Program.cs")))dir=dir.Parent;
  var web=Path.Combine(dir!.FullName,"src","Watchdog.TM.Web");
  var html=File.ReadAllText(Path.Combine(web,"wwwroot","index.html"));var js=File.ReadAllText(Path.Combine(web,"wwwroot","app.js"));var program=File.ReadAllText(Path.Combine(web,"Program.cs"));
  Check(html.Contains("id=\"asAdmin\"")&&html.Contains("Sign in as Administrator"),"sign-in page has a Sign in as Administrator box");
  Check(js.Contains("asAdministrator:asAdmin,username:asAdmin?'':"),"Administrator sign-in sends no user name");
  Check(js.Contains("$('username').disabled=$('asAdmin').checked"),"user name box is disabled while signing in as Administrator");
  Check(program.Contains("input.AsAdministrator||Access.IsAdministratorName(username)"),"server honours the Administrator box and the admin name");
  Check(program.Contains("Access.AdministratorPasswordHint"),"Administrator password typed with a user name gets a hint");
 }
}
