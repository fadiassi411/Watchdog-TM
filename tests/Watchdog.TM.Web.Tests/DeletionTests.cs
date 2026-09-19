using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Watchdog.TM;

public static class DeletionTests
{
 public static async Task Run()
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS deletion "+name);}
  var path=Path.Combine(Path.GetTempPath(),"tm-delete-"+Guid.NewGuid());
  var state=new ServerState(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string>{{"Storage:DataDirectory",path}}).Build());
  var c=state.Clone();c.Settings.Simulation=true;
  var plc=new Controller{Name="PLC preserved name",Enabled=false,Protocol=Protocol.TCP,Host="127.0.0.1"};var other=new Controller{Name="Unaffected",Enabled=false,Protocol=Protocol.TCP,Host="127.0.0.1"};
  var first=new Sensor{ControllerId=plc.Id,Name="Register A",TemperatureOffset=1};var second=new Sensor{ControllerId=plc.Id,Name="Register B",TemperatureOffset=2};var untouched=new Sensor{ControllerId=other.Id,Name="Keep",TemperatureOffset=3};
  c.Controllers.AddRange([plc,other]);c.Sensors.AddRange([first,second,untouched]);
  plc.History=new(){Enabled=true,Verification="synthetic deletion test",HeaderAddress=0,HeaderWords=5,BufferAddress=100,RecordWords=12,SequenceOffset=0,CountOffset=2,PositionOffset=3,StatusOffset=4,RecordSequenceOffset=0,RecordStatusOffset=2,IntervalOffset=3,TimestampOffsets=[4,5,6,7,8,9],Channels=[new(){SensorId=first.Id,Offset=10},new(){SensorId=second.Id,Offset=11}]};
  await state.Save(c,"fixture");
  state.Store.Sample(new(first.Id,DateTimeOffset.UtcNow,12.3,"VALID"));
  var stale=await DeletionEndpoints.Delete(state,first.Id,false,new("stale"));Check(((IStatusCodeHttpResult)stale).StatusCode==409,"stale revision rejected");
  Check(!Directory.GetFiles(path,"before-delete-*.db").Any(),"stale request has no mutation or backup");
  var missing=await DeletionEndpoints.Delete(state,Guid.NewGuid(),false,new(state.Revision));Check(((IStatusCodeHttpResult)missing).StatusCode==404,"unknown id rejected");
  var result=await DeletionEndpoints.Delete(state,first.Id,false,new(state.Revision));Check(((IStatusCodeHttpResult)result).StatusCode==200,"sensor deletion succeeds");
  Check(!state.Engine.Config.Sensors.Any(x=>x.Id==first.Id)&&state.Engine.Config.Sensors.Any(x=>x.Id==second.Id),"only selected sensor removed");
  var remaining=state.Engine.Config.Controllers.Single(x=>x.Id==plc.Id);Check(remaining.History.Enabled&&remaining.History.Channels.Single().SensorId==second.Id,"history mapping keeps other sensor");
  var history=PlcHistoryEndpoints.Rows(state,plc.Id,first.Id,null,null).Single(x=>x.Value==12.3);Check(history.Name==first.Name&&history.Plc==plc.Name&&history.Value==12.3,"history and ownership survive sensor deletion");
  var backups=Directory.GetFiles(path,"before-delete-*.db");Check(backups.Length==1&&new Store(backups[0]).Load().Sensors.Any(x=>x.Id==first.Id),"backup contains original configuration");
  await DeletionEndpoints.Delete(state,second.Id,false,new(state.Revision));Check(!state.Engine.Config.Controllers.Single(x=>x.Id==plc.Id).History.Enabled,"last mapped sensor disables history");
  await DeletionEndpoints.Delete(state,other.Id,true,new(state.Revision));Check(!state.Engine.Config.Controllers.Any(x=>x.Id==other.Id)&&!state.Engine.Config.Sensors.Any(x=>x.Id==untouched.Id),"controller cascades only its sensors");
  await DeletionEndpoints.Delete(state,plc.Id,true,new(state.Revision));
  Check(PlcHistoryEndpoints.Rows(state,plc.Id,first.Id,null,null).First().Plc==plc.Name,"controller deletion preserves export labels");
  await state.StopAsync(default);
  var restart=new ServerState(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string>{{"Storage:DataDirectory",path}}).Build());
  Check(restart.Engine.Config.Controllers.Count==0&&restart.Engine.Config.Sensors.Count==0,"deletions survive restart");
  await restart.StopAsync(default);
 }
}


