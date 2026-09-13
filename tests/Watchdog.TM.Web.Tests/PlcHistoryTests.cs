using Watchdog.TM;
using Microsoft.Extensions.Configuration;
public static class PlcHistoryTests
{
 public static async Task Run()
 {
  int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS PLC history "+message);checks++;}
  var config=new Configuration();config.Controllers.Clear();config.Sensors.Clear();
  foreach(var n in new[]{2,3,13}){
   var c=new Controller{Name="Synthetic "+n,Protocol=Protocol.TCP};config.Controllers.Add(c);
   c.History=new(){Enabled=true,Verification="SYNTHETIC TEST ONLY",HeaderAddress=0,HeaderWords=5,SequenceOffset=0,CountOffset=2,PositionOffset=3,StatusOffset=4,BufferAddress=100,RecordWords=n+11,RecordSequenceOffset=0,RecordStatusOffset=8,IntervalOffset=9,TimestampOffsets=[2,3,4,5,6,7]};
   for(int i=0;i<n;i++){var s=new Sensor{ControllerId=c.Id};config.Sensors.Add(s);c.History.Channels.Add(new(){SensorId=s.Id,Offset=11+i});}
   PlcHistory.Validate(c,config);
  }
  var path=Path.Combine(Path.GetTempPath(),"tm-history-"+Guid.NewGuid(),"test.db");var store=new Store(path);store.Save(config,"test","fixture");new AlarmNotifications(store);
  ushort[] Words(Controller c,uint sequence){var w=new ushort[c.History.RecordWords];w[0]=(ushort)sequence;w[1]=(ushort)(sequence>>16);var at=new DateTime(2026,1,1).AddMinutes(sequence*10);w[2]=(ushort)(at.Year-2000);w[3]=(ushort)at.Month;w[4]=(ushort)at.Day;w[5]=(ushort)at.Hour;w[6]=(ushort)at.Minute;w[7]=(ushort)at.Second;w[9]=600;foreach(var ch in c.History.Channels)w[ch.Offset]=unchecked((ushort)(short)-55);return w;}
  async Task<List<PlcSnapshot>> Fetch(Controller c,uint latest,int count,bool incoherent=false){var h=c.History;int reads=0;return await PlcHistory.Download(h,(a,n)=>{
   if(a==0){var seq=latest+(incoherent?(uint)(reads++):0);return Task.FromResult(new ushort[]{(ushort)seq,(ushort)(seq>>16),(ushort)count,(ushort)(latest%300),0});}
   var slot=(a-100)/h.RecordWords;var first=latest-(uint)count+1;var seqAt=Enumerable.Range(0,count).Select(i=>first+(uint)i).Single(x=>(x-1)%300==slot);return Task.FromResult(Words(c,seqAt));},CancellationToken.None);}
  foreach(var c in config.Controllers){var rows=await Fetch(c,300,300);Check(rows.Count==300&&rows[0].Values.Count==c.History.Channels.Count,"independent record width "+c.History.RecordWords);Check(PlcHistory.Import(store,c.Id,rows)==300,"50-hour buffer imported "+c.Name);Check(PlcHistory.Import(store,c.Id,rows)==0,"duplicate download ignored "+c.Name);var later=await Fetch(c,588,300);Check(PlcHistory.Import(store,c.Id,later)==288,"two-day recovery after wrap "+c.Name);Check(store.Samples(c.History.Channels[0].SensorId,DateTimeOffset.MinValue,DateTimeOffset.MaxValue).Count==588,"permanent retained history "+c.Name);}
  Check(PlcHistory.Import(new Store(path),config.Controllers[1].Id,await Fetch(config.Controllers[1],588,300))==0,"deduplication survives database reopen");
  var firstPlc=config.Controllers[0];Check(PlcHistory.Import(store,firstPlc.Id,await Fetch(firstPlc,1000,300))==300,"overflow imports remaining records");
  using(var db=store.Open()){using var q=db.CreateCommand();q.CommandText="SELECT last_sequence-first_sequence+1 FROM plc_gaps WHERE controller=$id";q.Parameters.AddWithValue("$id",firstPlc.Id.ToString());Check(Convert.ToInt64(q.ExecuteScalar())==112,"overwritten records detected exactly");q.Parameters.Clear();q.CommandText="SELECT (SELECT COUNT(*) FROM latest)+(SELECT COUNT(*) FROM alarms)+(SELECT COUNT(*) FROM email)+(SELECT COUNT(*) FROM mail_outbox)";Check(Convert.ToInt64(q.ExecuteScalar())==0,"historical imports never change live readings or create alarms/mail");}
  try{await Fetch(config.Controllers[1],600,300,true);throw new Exception("incoherent accepted");}catch(IOException){Check(true,"incoherent buffer rejected after bounded retries");}
  var outcomes=await Task.WhenAll(config.Controllers.Select(async(c)=>{try{if(c==firstPlc)throw new IOException("offline");return (await Fetch(c,600,300)).Count;}catch(IOException){return -1;}}));Check(outcomes.SequenceEqual(new[]{-1,300,300}),"independent PLC outage does not block others");Check((await Fetch(firstPlc,1001,300)).Count==300,"offline PLC recovers independently");
  try{PlcHistory.Import(store,firstPlc.Id,await Fetch(firstPlc,10,10));throw new Exception("reset accepted");}catch(IOException){Check(true,"sequence reset refused without corrupting history");}
  var wrong=config.Controllers[2].History.Channels[0].SensorId;config.Controllers[2].History.Channels[0].SensorId=config.Controllers[0].History.Channels[0].SensorId;try{PlcHistory.Validate(config.Controllers[2],config);throw new Exception("cross PLC map accepted");}catch(ArgumentException){Check(true,"cross-PLC sensor mapping refused");}config.Controllers[2].History.Channels[0].SensorId=wrong;
  var segmented=config.Controllers[2];segmented.History.Segments=[new(){FirstRecord=0,RecordCount=150,Address=100},new(){FirstRecord=150,RecordCount=150,Address=10000}];PlcHistory.Validate(segmented,config);
  var segmentedRows=await PlcHistory.Download(segmented.History,(a,n)=>{if(a==0)return Task.FromResult(new ushort[]{300,0,300,0,0});var slot=a>=10000?150+(a-10000)/24:(a-100)/24;return Task.FromResult(Words(segmented,(uint)slot+1));},CancellationToken.None);Check(segmentedRows.Count==300&&segmentedRows[^1].Sequence==300,"discontinuous Modbus buffer segments");segmented.History.Segments=[];
  var workerDir=Path.Combine(Path.GetTempPath(),"tm-worker-"+Guid.NewGuid());var workerStore=new Store(Path.Combine(workerDir,"watchdog.db"));config.Settings.Simulation=false;workerStore.Save(config,"test","worker fixture");
  var stateServer=new ServerState(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string>{{"Storage:DataDirectory",workerDir}}).Build());
  bool offline=true;int observed=0;stateServer.Engine.ReadingObserved+=(_,_)=>observed++;
  using var worker=new PlcHistoryWorker(stateServer,(c,a,n,ct)=>{if(c.Id==firstPlc.Id&&offline)throw new IOException("fixture offline");if(a==0)return Task.FromResult(new ushort[]{1,0,1,1,0});return Task.FromResult(Words(c,1));});
  await worker.StartAsync(CancellationToken.None);
  async Task Wait(Func<bool> ready){for(int i=0;i<100&&!ready();i++)await Task.Delay(50);Check(ready(),"worker scheduled independent download");}
  await Wait(()=>config.Controllers.Skip(1).All(c=>worker.Progress.GetValueOrDefault(c.Id)?.Status=="Ready"));
  Check(worker.Progress.GetValueOrDefault(firstPlc.Id)?.Status=="Retrying","real worker isolates offline PLC");offline=false;worker.Request(firstPlc.Id);await Wait(()=>worker.Progress.GetValueOrDefault(firstPlc.Id)?.Status=="Ready");
  Check(observed==0&&stateServer.Engine.Readings.Count==0,"worker imports bypass live and alarm observer");await worker.StopAsync(CancellationToken.None);
  Console.WriteLine("PLC history checks passed: "+checks);
 }
}
