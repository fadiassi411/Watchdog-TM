using Watchdog.TM;

// Ready-made history layouts for MicroBrain Delta DVP "History_Record" programs, so engineers do not type
// the register map by hand. The result is only a proposal: it is shown in the layout editor and must be
// reviewed and saved like any manual layout.
public static class PlcHistoryTemplates
{
 public record MappedChannel(Guid SensorId,string Name,string Register,int Offset,double Multiplier);
 public record SkippedSensor(Guid SensorId,string Name,string Reason);
 public record Proposal(PlcHistoryLayout Layout,MappedChannel[] Mapped,SkippedSensor[] Skipped,string Message,string[] PlcProgram);

 // History_Record program family (verified against Dar_ALHAWRA_F2A and F2B, Nov 2026):
 // header D4900 = status, D4901-D4902 = 32-bit sequence (low word first), D4903 = count, D4904 = next slot,
 // restored at power-up from the retained copies D3900-D3904.
 // Record: +0/+1 sequence, +2 status, +3 interval (600), +4..+9 year, month, day, hour, minute, second (TRD),
 // then one word per sensor copied from D100, D101, ... So a record has 10 + N words for N sensors.
 public const int FirstValueRegister=100, Channels=7, HeaderRegister=4900, BufferRegister=5000, RecordWords=17, FixedWords=10;
 // The DVP12SE only retains D408-D599 and D2000-D3919 through power loss and STOP->RUN (not adjustable).
 // Retained buffers start at D2000 and must end before D3900, which holds the retained sequence/count/slot.
 public const int RetainedBufferRegister=2000, RetainedWords=1900, MaximumRecords=144, MinimumRecords=6;
 public const int MaximumChannels=115; // a record must fit one Modbus read of 125 words
 public const string Buffer144="buffer144", RetainedAuto="retained", Retained111="retained111", Retained144Single="retained144single";
 public const int RetainedCapacity=111;

 // Records that fit the retained area for N sensors: at most 144 (24 hours at 10-minute intervals).
 public static int RetainedRecords(int channels)=>Math.Min(MaximumRecords,RetainedWords/(FixedWords+channels));

 public static Proposal Standard(Configuration config,Guid controllerId,int yearBase,string variant)=>variant switch
 {
  RetainedAuto=>Build(config,controllerId,yearBase,RetainedBufferRegister,null),
  Buffer144=>StandardDvp144(config,controllerId,yearBase),
  // Fixed variants kept for layouts created by V4.3.8 and the first V4.3.9 build.
  Retained111=>Build(config,controllerId,yearBase,RetainedBufferRegister,Channels),
  Retained144Single=>Build(config,controllerId,yearBase,RetainedBufferRegister,1),
  _=>throw new ArgumentException("Choose the PLC program version: retained buffer D2000 (sized for this PLC's sensors) or the old 7-sensor buffer D5000.")
 };

 // Delta DVP Modbus addressing: D0-D4095 at 0x1000 + n, D4096 and above at 0x9000 + (n - 4096).
 public static ushort DvpD(int register)=>register switch
 {
  >=0 and <4096=>(ushort)(0x1000+register),
  >=4096 and <=11999=>(ushort)(0x9000+register-4096),
  _=>throw new ArgumentOutOfRangeException(nameof(register),"Delta DVP data registers are D0-D11999.")
 };
 public static string? DvpName(ushort address)=>address switch
 {
  >=0x1000 and <0x2000=>"D"+(address-0x1000),
  >=0x9000 and <=0x9000+11999-4096=>"D"+(address-0x9000+4096),
  _=>null
 };
 static int? ValueIndex(Sensor s)=>s.TemperatureOffset is {} a&&DvpName(a) is {} name&&int.Parse(name[1..])-FirstValueRegister is var i&&i>=0&&i<MaximumChannels?i:null;

 public static Proposal StandardDvp144(Configuration config,Guid controllerId,int yearBase)=>Build(config,controllerId,yearBase,BufferRegister,Channels);

 // channels: null = count this PLC's sensors (highest of D100, D101, ... in use + 1).
 static Proposal Build(Configuration config,Guid controllerId,int yearBase,int bufferRegister,int? channels)
 {
  if(yearBase is not (0 or 2000))throw new ArgumentException("Choose the PLC year format: 4-digit (year base 0) or 2-digit (year base 2000).");
  var controller=config.Controllers.FirstOrDefault(x=>x.Id==controllerId)??throw new ArgumentException("Controller not found.");
  if(controller.Protocol==Protocol.Simulation)throw new ArgumentException("History requires a real PLC. Choose Modbus TCP or RTU for this controller first.");
  var sensors=config.Sensors.Where(x=>x.ControllerId==controllerId&&!x.Retired).OrderBy(x=>x.TemperatureOffset).ToList();
  int channelCount=channels??(sensors.Select(ValueIndex).Where(x=>x.HasValue).Select(x=>x!.Value+1).DefaultIfEmpty(0).Max());
  if(channelCount==0)throw new ArgumentException($"No active sensor on this controller reads D{FirstValueRegister} or the registers after it (Modbus {DvpD(FirstValueRegister)} and up). Set the sensor registers first, then load the standard layout.");
  var recordWords=FixedWords+channelCount;
  bool retained=bufferRegister==RetainedBufferRegister;
  int capacity=retained?RetainedRecords(channelCount):MaximumRecords;
  if(capacity<MinimumRecords)throw new ArgumentException($"{channelCount} sensors need {recordWords}-word records; only {capacity} fit in the retained area D2000-D3899. Split the sensors across PLC programs or use fewer sensors per PLC.");
  var existing=controller.History;bool configuredBefore=existing.HeaderAddress.HasValue;
  string Range(int n)=>n==1?$"D{FirstValueRegister}":$"D{FirstValueRegister}-D{FirstValueRegister+n-1}";
  var mapped=new List<MappedChannel>();var skipped=new List<SkippedSensor>();
  foreach(var sensor in sensors)
  {
   var register=sensor.TemperatureOffset is {} a?DvpName(a):null;var index=ValueIndex(sensor);
   if(index is null||index>=channelCount){skipped.Add(new(sensor.Id,sensor.Name,register is null?"No Delta D register set":$"{register} is not recorded by this program ({Range(channelCount)})"));continue;}
   if(mapped.Any(x=>x.Offset==FixedWords+index)){skipped.Add(new(sensor.Id,sensor.Name,$"{register} is already mapped to another sensor"));continue;}
   if(sensor.Format!=ValueFormat.Int16){skipped.Add(new(sensor.Id,sensor.Name,"History records signed 16-bit values; this sensor uses a different number format"));continue;}
   var multiplier=double.IsFinite(sensor.Multiplier)&&sensor.Multiplier!=0?sensor.Multiplier:0.1;
   mapped.Add(new(sensor.Id,sensor.Name,register!,FixedWords+index.Value,multiplier));
  }
  if(mapped.Count==0)throw new ArgumentException($"No active sensor on this controller reads {Range(channelCount)} with the signed 16-bit format. Set the sensor registers first, then load the standard layout.");
  var lastRegister=bufferRegister+capacity*recordWords-1;
  var layout=new PlcHistoryLayout
  {
   Enabled=true,
   Verification=$"MicroBrain DVP History_Record: {channelCount} sensor(s) {Range(channelCount)}, {capacity} x {recordWords} words, header D{HeaderRegister}, buffer D{bufferRegister}-D{lastRegister}{(retained?" (retained)":"")} (template V4.3.9)",
   Capacity=capacity,SyncSeconds=existing.SyncSeconds is >=10 and <=86400?existing.SyncSeconds:600,
   HeaderAddress=DvpD(HeaderRegister),HeaderWords=5,StatusOffset=0,SequenceOffset=1,CountOffset=3,PositionOffset=4,
   BufferAddress=DvpD(bufferRegister),Segments=[],RecordWords=recordWords,
   RecordSequenceOffset=0,RecordStatusOffset=2,IntervalOffset=3,TimestampOffsets=[4,5,6,7,8,9],
   YearBase=yearBase,
   // A new layout assumes the PLC clock is set to Lebanon winter time all year (DVP clocks do not change for summer time).
   UtcOffsetMinutes=configuredBefore?existing.UtcOffsetMinutes:120,TimeZoneId=configuredBefore?existing.TimeZoneId??"":"",
   LowWordFirst=true,HealthyStatus=0,
   Channels=mapped.Select(x=>new PlcHistoryChannel{SensorId=x.SensorId,Offset=x.Offset,Multiplier=x.Multiplier}).ToList()
  };
  PlcHistory.Validate(new Controller{Id=controller.Id,Protocol=controller.Protocol,History=layout},config);
  // The values the PLC program must use for this layout, written the way they appear in ISPSoft.
  var plc=new List<string>
  {
   $"Record size: MUL D4904 x {recordWords} -> D4940 (E0 = slot x {recordWords})",
   $"Record limit {capacity}: count check D4903 < {capacity}, slot wrap D4904 >= {capacity}, power-up safety checks D4903 > {capacity} and D4904 >= {capacity}",
   $"Record fields: DMOV D3900 -> D{bufferRegister}@E0 (sequence), D4942 -> D{bufferRegister+2}@E0 (status), 600 -> D{bufferRegister+3}@E0, D4930-D4935 -> D{bufferRegister+4}@E0 ... D{bufferRegister+9}@E0 (year ... second)",
   "Temperatures: "+string.Join(", ",Enumerable.Range(0,channelCount).Select(i=>$"D{FirstValueRegister+i} -> D{bufferRegister+FixedWords+i}@E0")),
   $"Status: one normally-closed contact per sensor ({(channelCount==1?"M20":$"M20 ... M{20+channelCount-1}")}) setting D4942 = 1",
   $"Buffer D{bufferRegister}-D{lastRegister}: "+(retained?"must be free and inside the retained range D2000-D3919 (ends before D3900)":"not retained, cleared by power cuts and STOP->RUN")
  };
  var clock=string.IsNullOrWhiteSpace(layout.TimeZoneId)?$"fixed UTC offset {(layout.UtcOffsetMinutes>=0?"+":"")}{layout.UtcOffsetMinutes} minutes":$"time zone {layout.TimeZoneId}";
  var message=$"{channelCount} sensor(s) on {Range(channelCount)}: {capacity} records of {recordWords} words (about {capacity/6.0:0.#} hours) at D{bufferRegister}-D{lastRegister}; {mapped.Count} sensor(s) mapped"+(skipped.Count>0?$", {skipped.Count} not mapped":"")+$". Clock: {clock}. Check the PLC program values below, use Test PLC History Header, then select Save history layout.";
  return new(layout,mapped.ToArray(),skipped.ToArray(),message,plc.ToArray());
 }
}
