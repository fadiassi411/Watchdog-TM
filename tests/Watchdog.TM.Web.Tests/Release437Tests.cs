using Microsoft.Extensions.Configuration;
using Watchdog.TM;

// Regression checks for the V4.3.7 standard PLC history layout.
public static class Release437Tests
{
 public static async Task Run()
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS 4.3.7 "+name);}
  void Rejects(Action action,string name){try{action();}catch(ArgumentException){Console.WriteLine("PASS 4.3.7 "+name);return;}throw new Exception(name);}

  Check(PlcHistoryTemplates.DvpD(100)==4196&&PlcHistoryTemplates.DvpD(4900)==37668&&PlcHistoryTemplates.DvpD(5000)==37768&&PlcHistoryTemplates.DvpD(4095)==8191&&PlcHistoryTemplates.DvpD(4096)==36864,"Delta DVP Modbus addressing");
  Check(PlcHistoryTemplates.DvpName(4196)=="D100"&&PlcHistoryTemplates.DvpName(37768)=="D5000"&&PlcHistoryTemplates.DvpName(10)==null,"Modbus address back to D register");

  var c=new Configuration();c.Settings.Simulation=true;
  var plc=new Controller{Name="F2A",Protocol=Protocol.TCP,Host="192.168.1.10",Enabled=false};c.Controllers.Add(plc);
  var sensors=Enumerable.Range(0,7).Select(i=>new Sensor{ControllerId=plc.Id,Name="TS"+(i+1),TemperatureOffset=(ushort)(4196+i),Multiplier=0.1}).ToList();
  sensors[6].Multiplier=0.01;
  var other=new Sensor{ControllerId=plc.Id,Name="Outside register",TemperatureOffset=4300};
  var retired=new Sensor{ControllerId=plc.Id,Name="Retired",TemperatureOffset=4196,Retired=true};
  c.Sensors.AddRange(sensors);c.Sensors.AddRange([other,retired]);

  var p=PlcHistoryTemplates.StandardDvp144(c,plc.Id,0);var h=p.Layout;
  Check(h.Enabled&&h.Capacity==144&&h.RecordWords==17&&h.HeaderAddress==37668&&h.HeaderWords==5&&h.BufferAddress==37768,"standard header and buffer addresses");
  Check(h.StatusOffset==0&&h.SequenceOffset==1&&h.CountOffset==3&&h.PositionOffset==4&&h.LowWordFirst&&h.HealthyStatus==0,"standard header fields");
  Check(h.RecordSequenceOffset==0&&h.RecordStatusOffset==2&&h.IntervalOffset==3&&h.TimestampOffsets.SequenceEqual([4,5,6,7,8,9]),"standard record fields");
  Check(h.Channels.Count==7&&h.Channels.Select(x=>x.Offset).SequenceEqual([10,11,12,13,14,15,16])&&h.Channels[0].SensorId==sensors[0].Id&&h.Channels[6].SensorId==sensors[6].Id,"sensors matched by D100-D106");
  Check(h.Channels[6].Multiplier==0.01&&h.Channels[0].Multiplier==0.1,"live multiplier reused for history");
  Check(p.Skipped.Length==1&&p.Skipped[0].Name=="Outside register"&&!p.Skipped.Any(x=>x.Name=="Retired"),"unmatched sensors reported, retired ignored");
  Check(h.YearBase==0&&h.UtcOffsetMinutes==120&&h.TimeZoneId=="","new layout uses 4-digit year and fixed +120");
  Check(PlcHistoryTemplates.StandardDvp144(c,plc.Id,2000).Layout.YearBase==2000,"2-digit year option");
  Rejects(()=>PlcHistoryTemplates.StandardDvp144(c,plc.Id,1900),"invalid year base rejected");
  Rejects(()=>PlcHistoryTemplates.StandardDvp144(c,Guid.NewGuid(),0),"unknown controller rejected");

  plc.History.HeaderAddress=1;plc.History.TimeZoneId="Asia/Beirut";plc.History.UtcOffsetMinutes=180;plc.History.SyncSeconds=1200;
  var kept=PlcHistoryTemplates.StandardDvp144(c,plc.Id,0).Layout;
  Check(kept.TimeZoneId=="Asia/Beirut"&&kept.UtcOffsetMinutes==180&&kept.SyncSeconds==1200,"existing clock and sync settings kept");

  var lone=new Configuration();var empty=new Controller{Protocol=Protocol.TCP,Host="127.0.0.1"};lone.Controllers.Add(empty);lone.Sensors.Add(new Sensor{ControllerId=empty.Id,TemperatureOffset=4300});
  Rejects(()=>PlcHistoryTemplates.StandardDvp144(lone,empty.Id,0),"controller without D100-D106 sensors rejected");
  var sim=new Configuration();var simPlc=new Controller{Protocol=Protocol.Simulation};sim.Controllers.Add(simPlc);
  Rejects(()=>PlcHistoryTemplates.StandardDvp144(sim,simPlc.Id,0),"simulation controller rejected");

  // The proposal must be accepted by the normal save path unchanged.
  var path=Path.Combine(Path.GetTempPath(),"tm-437-"+Guid.NewGuid());
  var state=new ServerState(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string>{{"Storage:DataDirectory",path}}).Build());
  var cfg=state.Clone();cfg.Settings.Simulation=true;plc.History=new();cfg.Controllers.Add(plc);cfg.Sensors.AddRange(sensors);
  await state.Save(cfg,"fixture");
  var proposal=PlcHistoryTemplates.StandardDvp144(state.Engine.Config,plc.Id,0);
  var next=state.Clone();next.Controllers.Single(x=>x.Id==plc.Id).History=System.Text.Json.JsonSerializer.Deserialize<PlcHistoryLayout>(System.Text.Json.JsonSerializer.Serialize(proposal.Layout))!;
  await state.Save(next,"template saved");
  Check(state.Engine.Config.Controllers.Single(x=>x.Id==plc.Id).History.Channels.Count==7,"proposal saves through the normal validation path");
 }
}
