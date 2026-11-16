using Watchdog.TM;

// V4.3.7: ready-made history layouts for verified MicroBrain PLC programs, so engineers do not
// type the register map by hand. The result is only a proposal: it is shown in the layout editor
// and must be reviewed and saved like any manual layout.
public static class PlcHistoryTemplates
{
 public record MappedChannel(Guid SensorId,string Name,string Register,int Offset,double Multiplier);
 public record SkippedSensor(Guid SensorId,string Name,string Reason);
 public record Proposal(PlcHistoryLayout Layout,MappedChannel[] Mapped,SkippedSensor[] Skipped,string Message);

 // Standard Delta DVP "History_Record" program (verified against Dar_ALHAWRA_F2A, 16 Nov 2026):
 // header D4900 = status, D4901-D4902 = 32-bit sequence (low word first), D4903 = count, D4904 = next slot;
 // 144 records of 17 words from D5000: +0/+1 sequence, +2 status, +3 interval (600),
 // +4..+9 year, month, day, hour, minute, second (TRD), +10..+16 temperatures copied from D100..D106.
 public const int FirstValueRegister=100, Channels=7, HeaderRegister=4900, BufferRegister=5000, RecordWords=17;
 // V4.3.8: the DVP12SE only retains D408-D599 and D2000-D3919 through power loss and STOP->RUN, which cannot
 // hold 144 records. The retained variant stores 111 records at D2000-D3886; D3900-D3904 keep sequence/count/slot.
 public const string Buffer144="buffer144", Retained111="retained111";
 public const int RetainedBufferRegister=2000, RetainedCapacity=111;
 public static Proposal Standard(Configuration config,Guid controllerId,int yearBase,string variant)=>variant switch
 {
  Buffer144=>StandardDvp144(config,controllerId,yearBase),
  Retained111=>Build(config,controllerId,yearBase,RetainedBufferRegister,RetainedCapacity,$"MicroBrain retained DVP History_Record: {RetainedCapacity} x {RecordWords} words, header D4900, buffer D2000-D3886 (retained), values D100-D106 (template V4.3.8)"),
  _=>throw new ArgumentException("Choose the PLC program version: buffer D5000 (144 records) or retained buffer D2000 (111 records).")
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

 public static Proposal StandardDvp144(Configuration config,Guid controllerId,int yearBase)=>
  Build(config,controllerId,yearBase,BufferRegister,144,"MicroBrain standard DVP History_Record: 144 x 17 words, header D4900, buffer D5000, values D100-D106 (template V4.3.7)");
 static Proposal Build(Configuration config,Guid controllerId,int yearBase,int bufferRegister,int capacity,string verification)
 {
  if(yearBase is not (0 or 2000))throw new ArgumentException("Choose the PLC year format: 4-digit (year base 0) or 2-digit (year base 2000).");
  var controller=config.Controllers.FirstOrDefault(x=>x.Id==controllerId)??throw new ArgumentException("Controller not found.");
  if(controller.Protocol==Protocol.Simulation)throw new ArgumentException("History requires a real PLC. Choose Modbus TCP or RTU for this controller first.");
  var existing=controller.History;bool configuredBefore=existing.HeaderAddress.HasValue;
  var mapped=new List<MappedChannel>();var skipped=new List<SkippedSensor>();
  foreach(var sensor in config.Sensors.Where(x=>x.ControllerId==controllerId&&!x.Retired).OrderBy(x=>x.TemperatureOffset))
  {
   var register=sensor.TemperatureOffset is {} a?DvpName(a):null;
   int index=register is null?-1:int.Parse(register[1..])-FirstValueRegister;
   if(index is < 0 or >= Channels){skipped.Add(new(sensor.Id,sensor.Name,register is null?"No Delta D register set":$"{register} is not one of D{FirstValueRegister}-D{FirstValueRegister+Channels-1} recorded by the standard program"));continue;}
   if(mapped.Any(x=>x.Offset==10+index)){skipped.Add(new(sensor.Id,sensor.Name,$"{register} is already mapped to another sensor"));continue;}
   if(sensor.Format!=ValueFormat.Int16){skipped.Add(new(sensor.Id,sensor.Name,"History records signed 16-bit values; this sensor uses a different number format"));continue;}
   var multiplier=double.IsFinite(sensor.Multiplier)&&sensor.Multiplier!=0?sensor.Multiplier:0.1;
   mapped.Add(new(sensor.Id,sensor.Name,register!,10+index,multiplier));
  }
  if(mapped.Count==0)throw new ArgumentException($"No active sensor on this controller reads D{FirstValueRegister}-D{FirstValueRegister+Channels-1} (Modbus {DvpD(FirstValueRegister)}-{DvpD(FirstValueRegister+Channels-1)}) with the signed 16-bit format. Set the sensor registers first, then load the standard layout.");
  var layout=new PlcHistoryLayout
  {
   Enabled=true,
   Verification=verification,
   Capacity=capacity,SyncSeconds=existing.SyncSeconds is >=10 and <=86400?existing.SyncSeconds:600,
   HeaderAddress=DvpD(HeaderRegister),HeaderWords=5,StatusOffset=0,SequenceOffset=1,CountOffset=3,PositionOffset=4,
   BufferAddress=DvpD(bufferRegister),Segments=[],RecordWords=RecordWords,
   RecordSequenceOffset=0,RecordStatusOffset=2,IntervalOffset=3,TimestampOffsets=[4,5,6,7,8,9],
   YearBase=yearBase,
   // A new layout assumes the PLC clock is set to Lebanon winter time all year (DVP clocks do not change for summer time).
   UtcOffsetMinutes=configuredBefore?existing.UtcOffsetMinutes:120,TimeZoneId=configuredBefore?existing.TimeZoneId??"":"",
   LowWordFirst=true,HealthyStatus=0,
   Channels=mapped.Select(x=>new PlcHistoryChannel{SensorId=x.SensorId,Offset=x.Offset,Multiplier=x.Multiplier}).ToList()
  };
  PlcHistory.Validate(new Controller{Id=controller.Id,Protocol=controller.Protocol,History=layout},config);
  var clock=string.IsNullOrWhiteSpace(layout.TimeZoneId)?$"fixed UTC offset {(layout.UtcOffsetMinutes>=0?"+":"")}{layout.UtcOffsetMinutes} minutes":$"time zone {layout.TimeZoneId}";
  var hours=capacity/6.0;
  var message=$"{capacity}-record layout (about {hours:0.#} hours, buffer D{bufferRegister}) loaded with {mapped.Count} of {Channels} channels"+(skipped.Count>0?$"; {skipped.Count} sensor(s) not mapped":"")+$". Clock: {clock}. Review it, use Test PLC History Header, then select Save history layout.";
  return new(layout,mapped.ToArray(),skipped.ToArray(),message);
 }
}
