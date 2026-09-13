using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Watchdog.TM;

public record PlcSnapshot(uint Sequence,DateTimeOffset At,int Status,int Interval,Dictionary<Guid,double?> Values,string Hash);
public static class PlcHistory
{
 public static void Validate(Controller c,Configuration config)
 {
  var h=c.History;if(!h.Enabled)return;
  void Need(bool ok,string message){if(!ok)throw new ArgumentException(message);}
  Need(c.Protocol!=Protocol.Simulation,"History requires a real PLC.");
  Need(!string.IsNullOrWhiteSpace(h.Verification),"Enter the verified PLC project/layout reference.");
  Need(h.Capacity==300&&h.SyncSeconds>=10&&h.SyncSeconds<=86400,"Use 300 records and synchronization from 10 to 86400 seconds.");
  Need(h.HeaderAddress.HasValue&&(h.BufferAddress.HasValue||h.Segments.Count>0),"Verified header and buffer addresses are required.");
  Need(h.HeaderWords>=5&&h.HeaderWords<=125&&h.RecordWords>=12&&h.RecordWords<=125,"Invalid header or record length.");
  Need(h.HeaderAddress+h.HeaderWords<=65536,"Header exceeds Modbus address range.");
  var segments=h.Segments.Count>0?h.Segments.OrderBy(x=>x.FirstRecord).ToList():new List<PlcHistorySegment>{new(){FirstRecord=0,RecordCount=h.Capacity,Address=h.BufferAddress!.Value}};
  int covered=0;foreach(var segment in segments){Need(segment.FirstRecord==covered&&segment.RecordCount>0&&segment.RecordCount<=300,"Segments must cover all records without logical gaps or overlaps.");covered+=segment.RecordCount;Need(segment.Address+segment.RecordCount*h.RecordWords<=65536,"Buffer segment exceeds Modbus range.");Need(h.HeaderAddress+h.HeaderWords<=segment.Address||segment.Address+segment.RecordCount*h.RecordWords<=h.HeaderAddress,"Header overlaps a buffer segment.");}
  Need(covered==h.Capacity,"Segments must contain exactly 300 records.");
  var physical=segments.OrderBy(x=>x.Address).ToArray();for(int i=1;i<physical.Length;i++)Need(physical[i-1].Address+physical[i-1].RecordCount*h.RecordWords<=physical[i].Address,"Physical buffer segments overlap.");
  var header=new[]{h.SequenceOffset,h.SequenceOffset+1,h.CountOffset,h.PositionOffset,h.StatusOffset};
  Need(header.All(x=>x>=0&&x<h.HeaderWords)&&header.Distinct().Count()==header.Length,"Header field offsets are invalid or overlap.");
  Need(h.TimestampOffsets.Length==6&&h.UtcOffsetMinutes>=-840&&h.UtcOffsetMinutes<=840&&(h.YearBase==0||h.YearBase==2000),"Specify six timestamp fields, year base and UTC offset.");
  var fields=new[]{h.RecordSequenceOffset,h.RecordSequenceOffset+1,h.RecordStatusOffset,h.IntervalOffset}.Concat(h.TimestampOffsets).Concat(h.Channels.Select(x=>x.Offset)).ToArray();
  Need(fields.All(x=>x>=0&&x<h.RecordWords)&&fields.Distinct().Count()==fields.Length,"Record fields overlap or exceed record size.");
  Need(h.Channels.Count>0&&h.Channels.Count<=18&&h.Channels.Select(x=>x.SensorId).Distinct().Count()==h.Channels.Count,"Map each sensor exactly once.");
  Need(h.Channels.All(x=>double.IsFinite(x.Multiplier)&&x.Multiplier!=0&&config.Sensors.Any(s=>s.Id==x.SensorId&&s.ControllerId==c.Id)),"History channels must reference this PLC's stable sensor IDs.");
 }
 static uint Sequence(ushort[] words,int offset,bool low)=>low?((uint)words[offset+1]<<16)|words[offset]:((uint)words[offset]<<16)|words[offset+1];
 public static async Task<List<PlcSnapshot>> Download(PlcHistoryLayout h,Func<ushort,ushort,Task<ushort[]>> read,CancellationToken ct)
 {
  for(int attempt=0;attempt<3;attempt++)
  {
   ct.ThrowIfCancellationRequested();var head=await read(h.HeaderAddress!.Value,(ushort)h.HeaderWords);
   var newest=Sequence(head,h.SequenceOffset,h.LowWordFirst);var count=head[h.CountOffset];var position=head[h.PositionOffset];
   if(head[h.StatusOffset]!=h.HealthyStatus||count>h.Capacity||position>=h.Capacity||newest<count){await Task.Delay(50,ct);continue;}
   var rows=new List<PlcSnapshot>();bool coherent=true;
   for(int i=0;i<count;i++)
   {
    ct.ThrowIfCancellationRequested();var slot=(position-count+i+h.Capacity)%h.Capacity;
    var segment=h.Segments.FirstOrDefault(x=>slot>=x.FirstRecord&&slot<x.FirstRecord+x.RecordCount);
    int address=segment==null?h.BufferAddress!.Value+slot*h.RecordWords:segment.Address+(slot-segment.FirstRecord)*h.RecordWords;
    var w=await read((ushort)address,(ushort)h.RecordWords);
    var sequence=Sequence(w,h.RecordSequenceOffset,h.LowWordFirst);
    if(sequence!=newest-count+1+i){coherent=false;break;}
    var t=h.TimestampOffsets.Select(x=>(int)w[x]).ToArray();
    DateTimeOffset at;
    try{at=new DateTimeOffset(t[0]+h.YearBase,t[1],t[2],t[3],t[4],t[5],TimeSpan.FromMinutes(h.UtcOffsetMinutes));}catch{throw new IOException("Invalid PLC timestamp; history was not imported.");}
    if(w[h.IntervalOffset]!=600)throw new IOException("PLC snapshot interval must be 600 seconds.");
    var status=w[h.RecordStatusOffset];
    var values=h.Channels.ToDictionary(x=>x.SensorId,x=>status==h.HealthyStatus?(double?)((short)w[x.Offset]*x.Multiplier):null);
    rows.Add(new(sequence,at,status,600,values,Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(w)))));
   }
   var end=await read(h.HeaderAddress.Value,(ushort)h.HeaderWords);
   if(coherent&&head.SequenceEqual(end))return rows;
  }
  throw new IOException("PLC buffer changed during download or is busy. Retrying later; no partial history imported.");
 }
 public static int Import(Store store,Guid controller,List<PlcSnapshot> rows)
 {
  int inserted=0;if(rows.Count==0)return 0;
  store.Write(db=>{
   using var q=db.CreateCommand();q.CommandText="SELECT MAX(sequence) FROM plc_snapshots WHERE controller=$id";q.Parameters.AddWithValue("$id",controller.ToString());var result=q.ExecuteScalar();long last=result is long n?n:0;
   if(rows[^1].Sequence<last)throw new IOException("PLC sequence moved backwards. Verify PLC reset/sequence retention before importing.");
   long missing=rows[0].Sequence>last+1?rows[0].Sequence-last-1:0;
   if(missing>0)Store.Run(db,"INSERT OR IGNORE INTO plc_gaps(controller,first_sequence,last_sequence,detected) VALUES($0,$1,$2,$3)",controller.ToString(),last+1,rows[0].Sequence-1,Store.Utc(DateTimeOffset.UtcNow));
   foreach(var row in rows){
    q.CommandText="SELECT hash FROM plc_snapshots WHERE controller=$id AND sequence=$seq";q.Parameters.Clear();q.Parameters.AddWithValue("$id",controller.ToString());q.Parameters.AddWithValue("$seq",(long)row.Sequence);
    if(q.ExecuteScalar() is string hash){if(hash!=row.Hash)throw new IOException("PLC reused a sequence with different data. Import stopped.");continue;}
    Store.Run(db,"INSERT INTO plc_snapshots(controller,sequence,at,hash,status,interval) VALUES($0,$1,$2,$3,$4,$5)",controller.ToString(),(long)row.Sequence,Store.Utc(row.At),row.Hash,row.Status,row.Interval);
    foreach(var value in row.Values)Store.Run(db,"INSERT INTO plc_history_values(controller,sequence,sensor,at,value,quality) VALUES($0,$1,$2,$3,$4,$5)",controller.ToString(),(long)row.Sequence,value.Key.ToString(),Store.Utc(row.At),value.Value,value.Value.HasValue?"VALID":"PLC STATUS "+row.Status);
    inserted++;
   }
  });return inserted;
 }
}
public record HistoryProgress(string Status,string Error,DateTimeOffset? LastSuccess,int Imported);
public sealed class PlcHistoryWorker(ServerState server,Func<Controller,ushort,ushort,CancellationToken,Task<ushort[]>>? reader=null):BackgroundService
{
 public ConcurrentDictionary<Guid,HistoryProgress> Progress {get;}=new();
 readonly ConcurrentDictionary<Guid,byte> manual=new();
 public void Request(Guid id)=>manual[id]=1;
 protected override async Task ExecuteAsync(CancellationToken ct)
 {
  var tasks=new Dictionary<Guid,Task>();
  try{while(!ct.IsCancellationRequested){foreach(var c in server.Engine.Config.Controllers)if(!tasks.ContainsKey(c.Id))tasks[c.Id]=Task.Run(()=>Loop(c.Id,ct),ct);await Task.Delay(1000,ct);}}
  finally{await Task.WhenAll(tasks.Values);}
 }
 async Task Loop(Guid id,CancellationToken ct)
 {
  var due=DateTimeOffset.MinValue;string prior="";
  while(!ct.IsCancellationRequested)
  {
   try{
    var config=server.Engine.Config;var c=config.Controllers.FirstOrDefault(x=>x.Id==id);
    if(c==null)return;
    var state=server.Engine.States.GetValueOrDefault(id)?.Status??"";bool recovered=state=="Connected"&&prior!="Connected";prior=state;
    if(c.Enabled&&c.History.Enabled&&!config.Settings.Simulation&&(DateTimeOffset.UtcNow>=due||recovered||manual.TryRemove(id,out _)))
    {
     PlcHistory.Validate(c,config);Progress[id]=new("Downloading","",Progress.GetValueOrDefault(id)?.LastSuccess,0);
     var rows=await PlcHistory.Download(c.History,(a,n)=>reader!=null?reader(c,a,n,ct):server.Engine.Transport.Session(c,m=>m.ReadHoldingRegisters(c.UnitId,a,n),ct),ct);
     // No configuration lock is held during network I/O. Reject results after edits/restores.
     await server.Gate.WaitAsync(ct);try{if(!ReferenceEquals(config,server.Engine.Config))throw new IOException("Configuration changed; retrying history download.");var n=PlcHistory.Import(server.Store,id,rows);Progress[id]=new("Ready","",DateTimeOffset.UtcNow,n);}finally{server.Gate.Release();}
     due=DateTimeOffset.UtcNow.AddSeconds(c.History.SyncSeconds);
    }
   }catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}
   catch(Exception ex){Progress[id]=new("Retrying",ex is IOException or ArgumentException?ex.Message:"History download failed. Check PLC connection and mapping.",Progress.GetValueOrDefault(id)?.LastSuccess,0);due=DateTimeOffset.UtcNow.AddSeconds(30);}
   await Task.Delay(1000,ct);
  }
 }
}
