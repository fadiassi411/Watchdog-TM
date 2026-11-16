using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Watchdog.TM;

// Regression checks for the V4.3.6 fixes.
public static class Release436Tests
{
 public static Task Run()
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS 4.3.6 "+name);}
  var beirut=new PlcHistoryLayout{TimeZoneId="Asia/Beirut"};var fixedOffset=new PlcHistoryLayout{UtcOffsetMinutes=120};
  var utc=TimeSpan.Zero;

  // Daylight-saving aware conversion.
  Check(PlcClock.ToUtc(new DateTime(2026,7,1,20,0,0),beirut)==new DateTimeOffset(2026,7,1,17,0,0,utc),"summer PLC local time uses UTC+3");
  Check(PlcClock.ToUtc(new DateTime(2026,1,15,20,0,0),beirut)==new DateTimeOffset(2026,1,15,18,0,0,utc),"winter PLC local time uses UTC+2");
  Check(PlcClock.ToUtc(new DateTime(2026,7,1,20,0,0),fixedOffset)==new DateTimeOffset(2026,7,1,18,0,0,utc),"blank time zone keeps fixed offset behaviour");
  Check(PlcClock.ToLocal(new DateTimeOffset(2026,7,1,17,0,0,utc),beirut)==new DateTime(2026,7,1,20,0,0),"UTC converts back to PLC summer local time");
  Check(PlcClock.Valid("Asia/Beirut")&&PlcClock.Valid("")&&PlcClock.Valid(null)&&!PlcClock.Valid("Not/AZone"),"time zone validation");
  Check(PlcClock.Same(new(){UtcOffsetMinutes=0,TimeZoneId="Asia/Beirut"},new(){UtcOffsetMinutes=120,TimeZoneId="asia/beirut"})&&!PlcClock.Same(beirut,fixedOffset),"clock comparison");

  // Repeated hour when clocks go back is resolved by record order.
  var zone=TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
  var repeated=Enumerable.Range(0,366*48).Select(i=>new DateTime(2026,1,1).AddMinutes(30*i)).First(zone.IsAmbiguousTime);
  var first=PlcClock.ToUtc(repeated,beirut);var second=PlcClock.ToUtc(repeated,beirut,first);
  Check(second-first==TimeSpan.FromHours(1),"repeated autumn hour maps to two increasing UTC instants");
  Check(PlcClock.ToUtc(repeated.AddMinutes(10),beirut,second)>second,"records after the repeated hour stay ordered");
  var skipped=Enumerable.Range(0,366*48).Select(i=>new DateTime(2026,1,1).AddMinutes(30*i)).First(zone.IsInvalidTime);
  Check(PlcClock.ToUtc(skipped,beirut)==new DateTimeOffset(skipped,zone.BaseUtcOffset).ToUniversalTime(),"skipped spring hour uses standard time instead of failing");

  // Stored history migration from the fixed +120 offset to the Beirut time zone.
  var path=Path.Combine(Path.GetTempPath(),"tm-436-"+Guid.NewGuid());Directory.CreateDirectory(path);
  var store=new Store(Path.Combine(path,"clock.db"));var controller=Guid.NewGuid();var sensor=Guid.NewGuid();
  var summer=new DateTimeOffset(2026,7,1,18,0,0,utc);var winter=new DateTimeOffset(2026,1,15,18,0,0,utc);
  PlcHistory.Import(store,controller,[new PlcSnapshot(1,winter,0,600,new(){{sensor,5}},"w"),new PlcSnapshot(2,summer,0,600,new(){{sensor,6}},"s")]);
  int updated=0;store.Write(db=>updated=PlcHistory.MigrateClock(db,controller,fixedOffset,beirut));
  Check(updated==2,"migration reports every snapshot");
  var stored=store.Samples(sensor,winter.AddDays(-1),summer.AddDays(1)).Select(x=>x.At).ToArray();
  Check(stored.Length==2&&stored[0]==winter&&stored[1]==summer.AddHours(-1),"migration moves only summer records by one hour");
  store.Write(db=>updated=PlcHistory.MigrateClock(db,controller,beirut,new(){UtcOffsetMinutes=0,TimeZoneId="Asia/Beirut"}));Check(updated==0,"unchanged clock migration is a no-op");

  // Streamed Excel export.
  var rows=Enumerable.Range(0,3).Select(i=>new PlcHistoryEndpoints.StoredHistory(controller,"PLC <1>",sensor,"Room\u0001 A",winter.AddMinutes(i).ToString("O"),i==1?null:20.5+i,"VALID",i,"PLC")).ToArray();
  var headers=new[]{"PLC ID","PLC","Sensor ID","Sensor","Timestamp UTC","Temperature C","Quality","PLC Sequence","Source"};
  var file=Path.Combine(path,"stream.xlsx");PlcHistoryEndpoints.WriteWorkbook(file,headers,rows,default);
  using(var book=new XLWorkbook(file)){var sheet=book.Worksheet(1);Check(sheet.Name=="History 1"&&sheet.Cell(1,6).GetString()=="Temperature C","streamed workbook has header row");Check(sheet.Cell(2,6).GetDouble()==20.5&&sheet.Cell(3,6).IsEmpty()&&sheet.Cell(4,2).GetString()=="PLC <1>"&&sheet.Cell(4,4).GetString()=="Room A","streamed workbook keeps values, blanks and cleans invalid characters");Check(sheet.LastRowUsed()!.RowNumber()==4,"streamed workbook includes every row");}
  var empty=Path.Combine(path,"empty.xlsx");PlcHistoryEndpoints.WriteWorkbook(empty,headers,[],default);
  using(var book=new XLWorkbook(empty))Check(book.Worksheets.Count==1&&book.Worksheet(1).Name=="History"&&book.Worksheet(1).Cell(1,1).GetString()=="PLC ID","empty export still has a header sheet");
  using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{PlcHistoryEndpoints.WriteWorkbook(Path.Combine(path,"cancel.xlsx"),headers,rows,cancel.Token);throw new Exception("cancelled export completed");}catch(OperationCanceledException){Console.WriteLine("PASS 4.3.6 streamed export honours cancellation");}}

  // Temporary download files are removed when the response stream closes.
  var temporary=Path.Combine(path,"download-test.db");File.WriteAllText(temporary,"x");
  using(var stream=TemporaryFiles.OpenAndDelete(temporary))Check(stream.ReadByte()=='x',"temporary download is readable");
  Check(!File.Exists(temporary),"temporary download deleted after streaming");

  // Safety backups are limited per kind; unrelated files are never touched.
  var data=Path.Combine(path,"server");var state=new ServerState(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Storage:DataDirectory",data}}).Build());
  for(int i=0;i<15;i++){var f=Path.Combine(data,$"before-delete-{i:00}.db");File.WriteAllText(f,"");File.SetLastWriteTimeUtc(f,DateTime.UtcNow.AddMinutes(i));}
  for(int i=0;i<3;i++)File.WriteAllText(Path.Combine(data,$"before-restore-{i}.db"),"");
  File.WriteAllText(Path.Combine(data,"before-web-migration-1.db"),"");
  state.PruneSafetyBackups();
  var kept=Directory.GetFiles(data,"before-delete-*.db").Select(Path.GetFileName).OrderBy(x=>x).ToArray();
  Check(kept.Length==ServerState.SafetyBackupsKept&&kept[0]=="before-delete-05.db","newest safety backups kept");
  Check(Directory.GetFiles(data,"before-restore-*.db").Length==3&&File.Exists(Path.Combine(data,"before-web-migration-1.db"))&&File.Exists(Path.Combine(data,"watchdog.db")),"other backups and the live database are untouched");
  Check(ServerState.MaximumRestoreBytes>100_000_000,"restore accepts backups beyond the 30 MB default request limit");
  return Task.CompletedTask;
 }
}
