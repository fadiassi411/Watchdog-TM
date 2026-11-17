using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Watchdog.TM;
using Proposal = PlcHistoryTemplates.Proposal;

// Regression checks for V4.3.9: confirmed record-format change and the 1-sensor layout.
public static class Release439Tests
{
 static PlcHistoryLayout Copy(PlcHistoryLayout h)=>System.Text.Json.JsonSerializer.Deserialize<PlcHistoryLayout>(System.Text.Json.JsonSerializer.Serialize(h))!;
 static int Status(IResult r)=>((IStatusCodeHttpResult)r).StatusCode??200;
 public static async Task Run()
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS 4.3.9 "+name);}

  var path=Path.Combine(Path.GetTempPath(),"tm-439-"+Guid.NewGuid());
  var state=new ServerState(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string>{{"Storage:DataDirectory",path}}).Build());
  var c=state.Clone();c.Settings.Simulation=true;
  var plc=new Controller{Name="F2B",Protocol=Protocol.TCP,Host="192.168.1.11",Enabled=false};c.Controllers.Add(plc);
  var d100=new Sensor{ControllerId=plc.Id,Name="F2B room",TemperatureOffset=4196,Multiplier=0.1};var d101=new Sensor{ControllerId=plc.Id,Name="F2B second",TemperatureOffset=4197,Multiplier=0.1};
  c.Sensors.AddRange([d100,d101]);await state.Save(c,"fixture");

  // 1-sensor template.
  var p=PlcHistoryTemplates.Standard(state.Engine.Config,plc.Id,0,PlcHistoryTemplates.Retained144Single);var single=p.Layout;
  Check(single.Capacity==144&&single.RecordWords==11&&single.BufferAddress==6096&&single.HeaderAddress==37668,"1-sensor template: 144 x 11 words at D2000, header D4900");
  Check(single.Channels.Count==1&&single.Channels[0].SensorId==d100.Id&&single.Channels[0].Offset==10,"1-sensor template maps only the D100 sensor");
  Check(p.Skipped.Length==1&&p.Skipped[0].SensorId==d101.Id&&p.Skipped[0].Reason.Contains("not recorded"),"D101 sensor reported as not recorded");
  Check(2000+144*11-1==3583,"1-sensor buffer ends at D3583, inside the retained area");
  var seven=PlcHistoryTemplates.Standard(state.Engine.Config,plc.Id,0,PlcHistoryTemplates.Retained111).Layout;
  Check(seven.RecordWords==17&&seven.Channels.Count==2,"7-sensor template unchanged (17 words)");

  // Flexible sizing: the retained template counts the PLC's sensors (D100, D101, ...).
  Proposal Auto(int sensors,params int[] skip)
  {
   var cfg=new Configuration();var ctl=new Controller{Protocol=Protocol.TCP,Host="10.0.0.1"};cfg.Controllers.Add(ctl);
   for(int i=0;i<sensors;i++)if(!skip.Contains(i))cfg.Sensors.Add(new Sensor{ControllerId=ctl.Id,Name="S"+i,TemperatureOffset=(ushort)(4196+i)});
   return PlcHistoryTemplates.Standard(cfg,ctl.Id,0,PlcHistoryTemplates.RetainedAuto);
  }
  foreach(var (count,records) in new[]{(1,144),(2,144),(3,144),(7,111),(8,105),(12,86),(18,67),(40,38)})
  {
   var a=Auto(count);var end=2000+a.Layout.Capacity*a.Layout.RecordWords-1;
   Check(a.Layout.RecordWords==10+count&&a.Layout.Capacity==records&&a.Layout.Channels.Count==count&&end<3900,$"{count} sensor(s): {records} records x {10+count} words, ends D{end}");
  }
  var gap=Auto(4,2);
  Check(gap.Layout.RecordWords==14&&gap.Layout.Channels.Select(x=>x.Offset).SequenceEqual([10,11,13]),"a missing register (D102) keeps its word so D103 stays at offset 13");
  var three=Auto(3);
  Check(three.PlcProgram.Any(x=>x.Contains("MUL D4904 x 13"))&&three.PlcProgram.Any(x=>x.Contains("D102 -> D2012@E0"))&&three.PlcProgram.Any(x=>x.Contains("M20 ... M22"))&&three.PlcProgram.Any(x=>x.Contains("D2000-D3871")),"PLC program values listed for a 3-sensor PLC");
  Check(PlcHistoryTemplates.Standard(state.Engine.Config,plc.Id,0,PlcHistoryTemplates.RetainedAuto).Layout.RecordWords==12,"2 sensors on D100-D101 give 12-word records");

  // Old F2B program: 12-word records with D100 and D101, buffer D5000.
  var old=Copy(single);old.RecordWords=12;old.BufferAddress=37768;old.Channels=[new(){SensorId=d100.Id,Offset=10,Multiplier=0.1},new(){SensorId=d101.Id,Offset=11,Multiplier=0.1}];
  var save=await PlcHistoryEndpoints.SaveMapping(state,plc.Id,new(state.Revision,old));Check(Status(save)==200,"initial 12-word layout saved");
  var t=new DateTimeOffset(2026,11,16,10,0,0,TimeSpan.Zero);
  PlcSnapshot Row(uint seq,string hash,params double[] v)=>new(seq,t.AddMinutes(10*seq),0,600,v.Select((x,i)=>(Id:i==0?d100.Id:d101.Id,x)).ToDictionary(x=>x.Id,x=>(double?)x.x),hash);
  Check(PlcHistory.Import(state.Store,plc.Id,[Row(1,"a1",20,21),Row(2,"a2",20.5,21.5),Row(3,"a3",21,22)])==3,"history imported in the old format");

  // Format change: refused until confirmed.
  var conflict=await PlcHistoryEndpoints.SaveMapping(state,plc.Id,new(state.Revision,Copy(single)));
  Check(Status(conflict)==409,"format change asks for confirmation (409)");
  var clock=Copy(single);clock.UtcOffsetMinutes=180;
  Check(Status(await PlcHistoryEndpoints.SaveMapping(state,plc.Id,new(state.Revision,clock,true)))==400,"clock change still refused even when confirmed");
  var forged=Copy(single);forged.ImportAfterSequence=999;
  var ok=await PlcHistoryEndpoints.SaveMapping(state,plc.Id,new(state.Revision,forged,true));
  var saved=state.Engine.Config.Controllers.Single(x=>x.Id==plc.Id).History;
  Check(Status(ok)==200&&saved.RecordWords==11&&saved.Channels.Count==1,"confirmed format change saved");
  Check(saved.ImportAfterSequence==3,"server sets the sequence floor to the newest imported record (browser value ignored)");
  Check(state.Store.Samples(d101.Id,t,t.AddDays(1)).Count==3&&state.Store.Samples(d100.Id,t,t.AddDays(1)).Count==3,"all previously imported samples kept, including the removed sensor");
  Check(state.Store.Audits().Any(x=>x.Contains("record format changed (12 -> 11 words, 2 -> 1 channel(s))")),"format change written to the audit log");

  // Same-format save keeps the floor; a browser cannot lower it.
  var lowered=Copy(saved);lowered.ImportAfterSequence=0;lowered.SyncSeconds=900;
  Check(Status(await PlcHistoryEndpoints.SaveMapping(state,plc.Id,new(state.Revision,lowered)))==200&&state.Engine.Config.Controllers.Single(x=>x.Id==plc.Id).History.ImportAfterSequence==3,"sequence floor survives later saves");

  // New-format imports start after the floor; re-read old sequences are ignored, not treated as conflicts.
  var floor=state.Engine.Config.Controllers.Single(x=>x.Id==plc.Id).History.ImportAfterSequence;
  var n=PlcHistory.Import(state.Store,plc.Id,[Row(2,"different-format",99),Row(3,"different-format",99),Row(4,"b4",22),Row(5,"b5",22.5)],floor);
  Check(n==2,"only records above the floor imported");
  var room=state.Store.Samples(d100.Id,t,t.AddDays(1));
  Check(room.Count==5&&room.First(x=>x.At==t.AddMinutes(20)).Temperature==20.5,"old samples unchanged after re-reading old sequences");

  // An 11-word buffer downloads correctly.
  var memory=new Dictionary<int,ushort>();void W(int a,int v)=>memory[a]=(ushort)v;
  W(37668,0);W(37669,7);W(37670,0);W(37671,2);W(37672,2);
  for(int slot=0;slot<2;slot++){var b=6096+slot*11;var seq=6+slot;W(b,seq);W(b+1,0);W(b+2,0);W(b+3,600);W(b+4,2026);W(b+5,11);W(b+6,16);W(b+7,12);W(b+8,slot*10);W(b+9,0);W(b+10,230+slot);}
  var dl=await PlcHistory.Download(saved,(a,len)=>Task.FromResult(Enumerable.Range(a,len).Select(x=>memory.GetValueOrDefault(x)).ToArray()),default);
  Check(dl.Rows.Count==2&&dl.Skipped==0&&dl.Rows[1].Sequence==7&&dl.Rows[1].Values[d100.Id]==23.1,"11-word records decode with one channel");
 }
}
