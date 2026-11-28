using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Watchdog.TM;

// V4.6.0: temperature blocks can be arranged on the dashboard; the order is saved on the server.
public static class Release460Tests
{
 public static async Task Run()
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS 4.6.0 "+name);}
  var path=Path.Combine(Environment.CurrentDirectory,"work","arrange-"+Guid.NewGuid());Directory.CreateDirectory(path);
  var s=new ServerState(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Storage:DataDirectory",path}}).Build());
  var c=s.Clone();var p=new Controller{Name="F2A"};c.Controllers.Add(p);
  foreach(var n in new[]{"A","B","C","D"})c.Sensors.Add(new Sensor{ControllerId=p.Id,Name=n});
  await s.Save(c,"test setup");
  Guid Id(string n)=>s.Engine.Config.Sensors.First(x=>x.Name==n).Id;
  string Names()=>string.Join("",DashboardLayout.Ordered(s.Engine.Config).Select(x=>x.Name));
  async Task<int> Status(Task<IResult> call)=>((await call) as IStatusCodeHttpResult)?.StatusCode??200;
  var admin=new Access.Principal(Access.AdminId,"Administrator",true,new HashSet<string>(),true,new HashSet<Guid>());

  Check(Names()=="ABCD","default order is the configuration order");
  Check(await Status(DashboardLayout.Save(s,admin,new(s.Revision,[Id("C"),Id("A"),Id("D"),Id("B")])))==200&&Names()=="CADB","arranged order saved");
  Check(s.Store.Audits()[0].Contains("Dashboard layout arranged: C, A, D, B"),"arrangement is audited");
  Check(await Status(DashboardLayout.Save(s,admin,new(s.Revision,[Id("C"),Id("A"),Id("D")])))==400&&Names()=="CADB","a missing block is refused and nothing changes");
  Check(await Status(DashboardLayout.Save(s,admin,new(s.Revision,[Id("C"),Id("A"),Id("A"),Id("B")])))==400,"a duplicated block is refused");
  Check(await Status(DashboardLayout.Save(s,admin,new("stale",[Id("A"),Id("B"),Id("C"),Id("D")])))==409,"stale revision is refused");

  c=s.Clone();c.Sensors.Add(new Sensor{ControllerId=p.Id,Name="E"});await s.Save(c,"add sensor");
  Check(Names()=="CADBE","a sensor added later appears at the end");
  c=s.Clone();c.Sensors.Remove(c.Sensors.First(x=>x.Name=="A"));await s.Save(c,"remove sensor");
  Check(Names()=="CDBE","a deleted sensor leaves the order without gaps");

  // A user who sees only some sensors rearranges those; everyone else's blocks keep their places.
  var scoped=new Access.Principal("u","nurse",false,new HashSet<string>{Access.Arrange},false,new HashSet<Guid>{Id("D"),Id("E")});
  Check(await Status(DashboardLayout.Save(s,scoped,new(s.Revision,[Id("E"),Id("D")])))==200&&Names()=="CEBD","limited user swaps only their own blocks");
  Check(await Status(DashboardLayout.Save(s,scoped,new(s.Revision,[Id("E"),Id("D"),Id("C"),Id("B")])))==400,"limited user cannot place sensors they cannot see");

  Check(await Status(DashboardLayout.Save(s,admin,new(s.Revision,[],true)))==200&&Names()=="BCDE"&&s.Engine.Config.Settings.DashboardOrder.Count==0,"reset returns to the configuration order and stores nothing");
  c=s.Clone();var retired=c.Sensors.First(x=>x.Name=="B");retired.Retired=true;await s.Save(c,"retire");
  Check(await Status(DashboardLayout.Save(s,admin,new(s.Revision,[Id("E"),Id("D"),Id("C")])))==200&&Names()=="BEDC","retired sensors, which have no block, keep their slot");

  var viewer=new Access.Principal("v","viewer",false,new HashSet<string>{Access.Trends},true,new HashSet<Guid>());
  Check(Access.Check(viewer,Access.Route("POST","/api/dashboard/order"),null)!.Contains("Arrange the dashboard"),"arranging needs its own checkbox");
  Check(Access.Check(scoped,Access.Route("POST","/api/dashboard/order"),null)==null,"a ticked level may arrange");

  var dir=new DirectoryInfo(AppContext.BaseDirectory);
  while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,"src","Watchdog.TM.Web","Program.cs")))dir=dir.Parent;
  var www=Path.Combine(dir!.FullName,"src","Watchdog.TM.Web","wwwroot");
  var html=File.ReadAllText(Path.Combine(www,"index.html"));var js=File.ReadAllText(Path.Combine(www,"app.js"));var arrange=File.ReadAllText(Path.Combine(www,"arrange.js"));
  Check(html.Contains("arrange.js"),"arrange script is loaded");
  Check(js.Contains("data-arrange-start")&&js.Contains("can('arrange-dashboard')"),"Arrange blocks button shows only for permitted users");
  Check(js.Contains("if(!state||arranging||"),"live refresh pauses while arranging");
  Check(arrange.Contains("data-move=\"-1\"")&&arrange.Contains("dragover")&&arrange.Contains("api('dashboard/order'"),"blocks move by drag or arrow buttons and save to the server");
 }
}
