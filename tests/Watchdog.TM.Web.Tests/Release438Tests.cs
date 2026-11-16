using Watchdog.TM;

// Regression checks for the V4.3.8 retained (111-record) DVP history buffer.
public static class Release438Tests
{
 public static Task Run()
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS 4.3.8 "+name);}
  void Rejects(Action action,string name){try{action();}catch(ArgumentException){Console.WriteLine("PASS 4.3.8 "+name);return;}throw new Exception(name);}

  var c=new Configuration();var plc=new Controller{Name="F2A",Protocol=Protocol.TCP,Host="192.168.1.10"};c.Controllers.Add(plc);
  c.Sensors.AddRange(Enumerable.Range(0,7).Select(i=>new Sensor{ControllerId=plc.Id,Name="TS"+(i+1),TemperatureOffset=(ushort)(4196+i),Multiplier=0.1}));

  var retained=PlcHistoryTemplates.Standard(c,plc.Id,0,PlcHistoryTemplates.Retained111).Layout;
  Check(retained.Capacity==111&&retained.BufferAddress==PlcHistoryTemplates.DvpD(2000)&&retained.BufferAddress==6096&&retained.HeaderAddress==37668&&retained.RecordWords==17,"retained template uses D2000 x 111 and header D4900");
  var lastWord=2000+111*17-1;
  Check(lastWord==3886&&lastWord<3900&&2000>=2000,"retained buffer D2000-D3886 stays inside D2000-D3919 and below D3900");
  Check(retained.Channels.Count==7&&retained.Verification.Contains("retained"),"retained template maps all sensors");
  var standard=PlcHistoryTemplates.Standard(c,plc.Id,0,PlcHistoryTemplates.Buffer144).Layout;
  Check(standard.Capacity==144&&standard.BufferAddress==37768,"144-record template unchanged");
  Rejects(()=>PlcHistoryTemplates.Standard(c,plc.Id,0,"unknown"),"unknown program version rejected");

  // Validation accepts 111 and still rejects other sizes.
  plc.History=retained;PlcHistory.Validate(plc,c);Console.WriteLine("PASS 4.3.8 111-record layout validates");
  foreach(var bad in new[]{110,112}){var copy=System.Text.Json.JsonSerializer.Deserialize<PlcHistoryLayout>(System.Text.Json.JsonSerializer.Serialize(retained))!;copy.Capacity=bad;plc.History=copy;Rejects(()=>PlcHistory.Validate(plc,c),$"capacity {bad} rejected");}

  // Moving the buffer keeps imported history valid; changing how records decode does not.
  Check(PlcHistory.SameRecordFormat(standard,retained),"buffer relocation D5000x144 -> D2000x111 allowed with imported history");
  var channelChanged=System.Text.Json.JsonSerializer.Deserialize<PlcHistoryLayout>(System.Text.Json.JsonSerializer.Serialize(retained))!;channelChanged.Channels[0].Multiplier=0.01;
  Check(!PlcHistory.SameRecordFormat(standard,channelChanged),"channel scaling change still refused");
  var headerChanged=System.Text.Json.JsonSerializer.Deserialize<PlcHistoryLayout>(System.Text.Json.JsonSerializer.Serialize(retained))!;headerChanged.HeaderAddress=37000;
  Check(!PlcHistory.SameRecordFormat(standard,headerChanged),"header move still refused");
  var clockChanged=System.Text.Json.JsonSerializer.Deserialize<PlcHistoryLayout>(System.Text.Json.JsonSerializer.Serialize(retained))!;clockChanged.UtcOffsetMinutes=180;
  Check(!PlcHistory.SameRecordFormat(standard,clockChanged),"clock change still refused (use timestamp correction)");
  return DownloadWrapped(retained,Check);
 }

 // Simulates a full retained buffer that has wrapped: 111 records, next slot 5, newest sequence 500.
 static async Task DownloadWrapped(PlcHistoryLayout h,Action<bool,string> Check)
 {
  var memory=new Dictionary<int,ushort>();
  const uint newest=500;const int position=5,count=111;
  void Word(int address,int value)=>memory[address]=(ushort)value;
  Word(37668,0);Word(37669,(int)(newest&0xFFFF));Word(37670,(int)(newest>>16));Word(37671,count);Word(37672,position);
  for(int i=0;i<count;i++)
  {
   var slot=(position-count+i+111)%111;var seq=newest-count+1+(uint)i;var at=new DateTime(2026,11,16,0,0,0).AddMinutes(10*i);var b=6096+slot*17;
   Word(b,(int)(seq&0xFFFF));Word(b+1,(int)(seq>>16));Word(b+2,0);Word(b+3,600);
   Word(b+4,at.Year);Word(b+5,at.Month);Word(b+6,at.Day);Word(b+7,at.Hour);Word(b+8,at.Minute);Word(b+9,at.Second);
   for(int ch=0;ch<7;ch++)Word(b+10+ch,200+ch);
  }
  Check(6096+110*17+16==6096+1886,"last retained slot ends at D3886 (Modbus 7982)");
  var result=await PlcHistory.Download(h,(a,n)=>Task.FromResult(Enumerable.Range(a,n).Select(x=>memory.GetValueOrDefault(x)).ToArray()),default);
  Check(result.Rows.Count==111&&result.Skipped==0,"wrapped 111-record buffer downloads every record");
  Check(result.Rows[0].Sequence==390&&result.Rows[^1].Sequence==500,"records come back oldest to newest across the wrap");
  Check(result.Rows[0].At==new DateTimeOffset(2026,11,16,0,0,0,TimeSpan.FromMinutes(h.UtcOffsetMinutes)).ToUniversalTime()&&result.Rows[0].Values.Values.First()==20.0,"timestamps and 0.1 scaling decoded");
 }
}
