using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Watchdog.TM;

public static class History144Tests
{
 public static async Task Run()
 {
  int checks=0;
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS 144 history "+name);checks++;}
  Check(new PlcHistoryLayout().Capacity==144,"new layouts default to one day");
  Check(JsonSerializer.Deserialize<PlcHistoryLayout>("{\"Capacity\":300}")!.Capacity==300,"existing 300 layout preserved");
  var config=new Configuration();config.Controllers.Clear();config.Sensors.Clear();
  foreach(var (floor,n) in new[]{("2nd",15),("1st",3),("GF",5),("B1",1)})
  {
   var plc=new Controller{Name=floor,Protocol=Protocol.TCP,Enabled=false};
   plc.History=new(){Enabled=true,Verification="SYNTHETIC ONLY",HeaderAddress=0,HeaderWords=5,SequenceOffset=0,CountOffset=2,PositionOffset=3,StatusOffset=4,BufferAddress=100,RecordWords=n+11,RecordSequenceOffset=0,RecordStatusOffset=8,IntervalOffset=9,TimestampOffsets=[2,3,4,5,6,7]};
   config.Controllers.Add(plc);
   for(int i=0;i<n;i++){var sensor=new Sensor{Name=floor+"-"+i,ControllerId=plc.Id};config.Sensors.Add(sensor);plc.History.Channels.Add(new(){SensorId=sensor.Id,Offset=11+i});}
   PlcHistory.Validate(plc,config);
  }
  DateTimeOffset At(uint sequence)=>new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero).AddMinutes(sequence*10);
  ushort[] Record(Controller c,uint seq){var w=new ushort[c.History.RecordWords];w[0]=(ushort)seq;w[1]=(ushort)(seq>>16);var t=At(seq);w[2]=(ushort)(t.Year-2000);w[3]=(ushort)t.Month;w[4]=(ushort)t.Day;w[5]=(ushort)t.Hour;w[6]=(ushort)t.Minute;w[7]=(ushort)t.Second;w[9]=600;foreach(var ch in c.History.Channels)w[ch.Offset]=unchecked((ushort)(short)-180);return w;}
  Task<List<PlcSnapshot>> Fetch(Controller c,uint latest,int count,bool torn=false,int? badCount=null,int? badPosition=null)
  {
   int headerReads=0;return PlcHistory.Download(c.History,(address,length)=>{
    if(address==0){var seq=latest+(torn?(uint)headerReads++:0);return Task.FromResult(new ushort[]{(ushort)seq,(ushort)(seq>>16),(ushort)(badCount??count),(ushort)(badPosition??(int)(latest%144)),0});}
    var segment=c.History.Segments.FirstOrDefault(x=>address>=x.Address&&address<x.Address+x.RecordCount*c.History.RecordWords);
    int slot=segment==null?(address-100)/c.History.RecordWords:segment.FirstRecord+(address-segment.Address)/c.History.RecordWords;
    uint seqAt=Enumerable.Range(0,count).Select(i=>latest-(uint)count+1+(uint)i).Single(x=>(x-1)%144==slot);
    return Task.FromResult(Record(c,seqAt));
   },CancellationToken.None);
  }
  var path=Path.Combine(Path.GetTempPath(),"tm-history144-"+Guid.NewGuid(),"watchdog.db");var store=new Store(path);store.Save(config,"test","144 fixture");new AlarmNotifications(store);
  foreach(var plc in config.Controllers)
  {
   Check((await Fetch(plc,0,0)).Count==0,"empty buffer "+plc.Name);
   Check((await Fetch(plc,3,3)).Count==3,"partial buffer "+plc.Name);
   var rows=await Fetch(plc,144,144);
   Check(rows.Count==144&&rows[0].Sequence==1&&rows[^1].Sequence==144&&rows.All(x=>x.Values.Count==plc.History.Channels.Count),"full per-floor layout "+plc.Name);
   Check(rows[0].At==At(1)&&rows[0].Values.Values.All(x=>x==-18),"PLC timestamps and signed temperatures "+plc.Name);
   Check(PlcHistory.Import(store,plc.Id,rows)==144,"initial download "+plc.Name);
   Check(PlcHistory.Import(store,plc.Id,rows)==0,"duplicate ignored "+plc.Name);
   Check(PlcHistory.Import(store,plc.Id,await Fetch(plc,288,144))==144,"24-hour recovery at wrap "+plc.Name);
   Check(store.Samples(plc.History.Channels[0].SensorId,DateTimeOffset.MinValue,DateTimeOffset.MaxValue).Count==288,"Watchdog retains more than PLC capacity "+plc.Name);
  }
  var first=config.Controllers[0];Check(PlcHistory.Import(new Store(path),first.Id,await Fetch(first,288,144))==0,"deduplication survives reopen");
  Check(PlcHistory.Import(store,first.Id,await Fetch(first,500,144))==144,"only newest day recovered after longer outage");
  using(var db=store.Open()){using var q=db.CreateCommand();q.CommandText="SELECT last_sequence-first_sequence+1 FROM plc_gaps";Check(Convert.ToInt64(q.ExecuteScalar())==68,"exact overwrite gap detected");q.CommandText="SELECT (SELECT COUNT(*) FROM latest)+(SELECT COUNT(*) FROM alarms)+(SELECT COUNT(*) FROM email)+(SELECT COUNT(*) FROM mail_outbox)";Check(Convert.ToInt64(q.ExecuteScalar())==0,"imports never touch live values or alarms/email");}
  foreach(var mode in new[]{"torn","count","position"}){try{await Fetch(first,500,144,mode=="torn",mode=="count"?145:null,mode=="position"?144:null);throw new Exception("Invalid header accepted");}catch(IOException){Check(true,"reject "+mode);}}
  first.History.Segments=[new(){FirstRecord=0,RecordCount=72,Address=100},new(){FirstRecord=72,RecordCount=72,Address=10000}];PlcHistory.Validate(first,config);
  Check((await Fetch(first,500,144)).Select(x=>x.Sequence).SequenceEqual(Enumerable.Range(357,144).Select(x=>(uint)x)),"segmented 144 buffer across wrap");
  first.History.Segments[1].RecordCount=73;try{PlcHistory.Validate(first,config);throw new Exception("Wrong segment size accepted");}catch(ArgumentException){Check(true,"segments must total configured capacity");}first.History.Segments=[];
  foreach(var invalid in new[]{0,143,145,301}){first.History.Capacity=invalid;try{PlcHistory.Validate(first,config);throw new Exception("Invalid capacity accepted");}catch(ArgumentException){Check(true,"unsupported capacity rejected "+invalid);}}first.History.Capacity=144;
  config.Settings.Simulation=false;foreach(var plc in config.Controllers)plc.Enabled=true;
  store.Save(config,"test","worker fixture");var state=new ServerState(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string>{{"Storage:DataDirectory",Path.GetDirectoryName(path)!}}).Build());
  bool offline=true;int observations=0;state.Engine.ReadingObserved+=(_,_)=>observations++;
  using var worker=new PlcHistoryWorker(state,(plc,a,n,ct)=>{if(plc.Id==first.Id&&offline)throw new IOException("synthetic outage");if(a==0)return Task.FromResult(new ushort[]{501,0,1,(ushort)(501%144),0});return Task.FromResult(Record(plc,501));});
  async Task Wait(Func<bool> ready){for(int i=0;i<100&&!ready();i++)await Task.Delay(50);Check(ready(),"independent worker completion");}
  await worker.StartAsync(default);
  try{await Wait(()=>config.Controllers.Skip(1).All(x=>worker.Progress.GetValueOrDefault(x.Id)?.Status=="Ready"));Check(worker.Progress.GetValueOrDefault(first.Id)?.Status=="Retrying","one floor outage does not block three other floors");offline=false;worker.Request(first.Id);await Wait(()=>worker.Progress.GetValueOrDefault(first.Id)?.Status=="Ready");Check(observations==0&&state.Engine.Readings.Count==0,"recovery bypasses live/alarm observation");}
  finally{await worker.StopAsync(default);}
  Console.WriteLine("144 history checks passed: "+checks);
 }
}
