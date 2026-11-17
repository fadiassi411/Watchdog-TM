using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Watchdog.TM;
public record HistoryMappingInput(string Revision,PlcHistoryLayout Layout,bool ConfirmFormatChange=false);
// TimeZoneId (V4.3.6): blank or missing keeps the fixed UTC offset; an IANA zone such as Asia/Beirut follows daylight saving.
public record HistoryUtcOffsetMigrationInput(string Revision,int UtcOffsetMinutes,string? TimeZoneId=null);
public static class PlcHistoryEndpoints
{
 public static void MapPlcHistory(this RouteGroupBuilder api)
 {
  api.MapGet("/plc-history",(ServerState s,PlcHistoryWorker worker)=>Results.Ok(new{revision=s.Revision,controllers=s.Engine.Config.Controllers.Select(c=>new{c.Id,c.Name,c.History,progress=worker.Progress.GetValueOrDefault(c.Id)}),sensors=s.Engine.Config.Sensors.Select(x=>new{x.Id,x.Name,x.ControllerId}),gaps=Gaps(s.Store)}));
  api.MapPost("/plc-history/{id:guid}/mapping",(Guid id,HistoryMappingInput input,ServerState s)=>SaveMapping(s,id,input));
  api.MapPost("/plc-history/{id:guid}/migrate-utc-offset",async(Guid id,HistoryUtcOffsetMigrationInput input,ServerState s)=>{await s.Gate.WaitAsync();try{if(input.Revision!=s.Revision)return Results.Conflict(new{error="Configuration changed. Reload and retry."});if(input.UtcOffsetMinutes is < -840 or > 840)return Results.BadRequest(new{error="UTC offset must be between -840 and 840 minutes."});var c=s.Clone();var controller=c.Controllers.SingleOrDefault(x=>x.Id==id);if(controller==null)return Results.NotFound();if(!controller.History.Enabled)return Results.BadRequest(new{error="Enable and verify the PLC history layout first."});var oldClock=new PlcHistoryLayout{UtcOffsetMinutes=controller.History.UtcOffsetMinutes,TimeZoneId=controller.History.TimeZoneId??""};var newClock=new PlcHistoryLayout{UtcOffsetMinutes=input.UtcOffsetMinutes,TimeZoneId=input.TimeZoneId?.Trim()??""};if(!PlcClock.Valid(newClock.TimeZoneId))return Results.BadRequest(new{error="Unknown PLC time zone. Use an IANA name such as Asia/Beirut, or leave it blank for a fixed UTC offset."});string Describe(PlcHistoryLayout x)=>string.IsNullOrWhiteSpace(x.TimeZoneId)?$"UTC offset {x.UtcOffsetMinutes} minutes":$"time zone {x.TimeZoneId}";if(PlcClock.Same(oldClock,newClock)&&oldClock.UtcOffsetMinutes==newClock.UtcOffsetMinutes)return Results.Ok(new{message="The PLC clock is already set to that value.",updated=0});long count;using(var db=s.Store.Open()){using var q=db.CreateCommand();q.CommandText="SELECT COUNT(*) FROM plc_snapshots WHERE controller=$id";q.Parameters.AddWithValue("$id",id.ToString());count=Convert.ToInt64(q.ExecuteScalar());}if(count==0)return Results.BadRequest(new{error="No imported PLC history exists. Change utcOffsetMinutes or timeZoneId in the layout and save it normally."});var backup=Path.Combine(s.DirectoryPath,$"before-plc-history-offset-{id:N}-{DateTime.UtcNow:yyyyMMddHHmmss}.db");s.Store.Backup(backup);s.PruneSafetyBackups();controller.History.UtcOffsetMinutes=newClock.UtcOffsetMinutes;controller.History.TimeZoneId=newClock.TimeZoneId;PlcHistory.Validate(controller,c);int updated=0;await s.Save(c,$"Migrated PLC history clock from {Describe(oldClock)} to {Describe(newClock)}; preserved sequences; backup {Path.GetFileName(backup)}",db=>updated=PlcHistory.MigrateClock(db,id,oldClock,newClock));return Results.Ok(new{message=$"Updated {updated} PLC snapshot timestamp(s). Backup created: {Path.GetFileName(backup)}.",updated,backup=Path.GetFileName(backup)});}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}finally{s.Gate.Release();}});
  // V4.3.7: proposal only; nothing is saved until the engineer reviews it and selects Save history layout.
  api.MapGet("/plc-history/{id:guid}/standard-layout",(Guid id,int? yearBase,string? variant,ServerState s)=>{try{return Results.Ok(PlcHistoryTemplates.Standard(s.Engine.Config,id,yearBase??0,variant??PlcHistoryTemplates.Buffer144));}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}});
  api.MapPost("/plc-history/{id:guid}/download",(Guid id,ServerState s,PlcHistoryWorker worker)=>{var c=s.Engine.Config.Controllers.SingleOrDefault(x=>x.Id==id);if(c==null||!c.Enabled||!c.History.Enabled||s.Engine.Config.Settings.Simulation)return Results.BadRequest(new{error="Enable a verified history layout on a live controller first."});worker.Request(id);return Results.Ok(new{message="Download queued for this PLC."});});
  api.MapPost("/plc-history/{id:guid}/test-header",async(Guid id,ServerState s,CancellationToken ct)=>{var c=s.Engine.Config.Controllers.SingleOrDefault(x=>x.Id==id);if(c==null||!c.Enabled||!c.History.Enabled||s.Engine.Config.Settings.Simulation)return Results.BadRequest(new{error="Enable a verified history layout on a live controller first."});try{PlcHistory.Validate(c,s.Engine.Config);var address=c.History.HeaderAddress!.Value;var words=await s.Engine.Transport.Session(c,m=>m.ReadHoldingRegisters(c.UnitId,address,(ushort)c.History.HeaderWords),ct);return Results.Ok(new{address,words});}catch(Exception ex){return Results.BadRequest(new{error=$"History header read failed: {ex.GetBaseException().Message}"});}});
  api.MapGet("/stored-history",(Guid? controller,Guid? sensor,DateTimeOffset? from,DateTimeOffset? to,ServerState s)=>Results.Ok(Rows(s,controller,sensor,from,to).Take(2001).ToArray()));
  api.MapGet("/stored-history/export",async(HttpContext ctx,Guid? controller,Guid? sensor,DateTimeOffset? from,DateTimeOffset? to,string format,ServerState s)=>{
   if(format!="csv"&&format!="xlsx"){ctx.Response.StatusCode=400;return;}
   var headers=new[]{"PLC ID","PLC","Sensor ID","Sensor","Timestamp UTC","Temperature C","Quality","PLC Sequence","Source"};
   if(format=="csv"){
    ctx.Response.ContentType="text/csv; charset=utf-8";ctx.Response.Headers.ContentDisposition="attachment; filename=Watchdog-stored-history.csv";
    await using var writer=new StreamWriter(ctx.Response.Body,new UTF8Encoding(true),leaveOpen:true);
    await writer.WriteLineAsync(string.Join(",",headers));
    foreach(var row in Rows(s,controller,sensor,from,to)){ctx.RequestAborted.ThrowIfCancellationRequested();await writer.WriteLineAsync(string.Join(",",Cells(row).Select(Csv)));}
   }else{
    // V4.3.6: rows stream into a temporary workbook file instead of memory, so exports of years of history stay within RAM.
    var path=Path.Combine(s.DirectoryPath,"export-"+Guid.NewGuid()+".xlsx");
    try{await Task.Run(()=>WriteWorkbook(path,headers,Rows(s,controller,sensor,from,to),ctx.RequestAborted),ctx.RequestAborted);}catch{TemporaryFiles.Delete(path);throw;}
    await using var file=TemporaryFiles.OpenAndDelete(path);
    ctx.Response.ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";ctx.Response.Headers.ContentDisposition="attachment; filename=Watchdog-stored-history.xlsx";ctx.Response.ContentLength=file.Length;
    await file.CopyToAsync(ctx.Response.Body,ctx.RequestAborted);
   }
  });
 }
 public static void WriteWorkbook(string path,string[] headers,IEnumerable<StoredHistory> rows,CancellationToken ct)
 {
  const int MaximumSheetRows=1048576;
  using var document=SpreadsheetDocument.Create(path,SpreadsheetDocumentType.Workbook);
  var workbook=document.AddWorkbookPart();var sheets=new List<string>();OpenXmlWriter? writer=null;int line=0;
  static string Clean(string value)=>new(value.Where(System.Xml.XmlConvert.IsXmlChar).ToArray());
  void Row(IEnumerable<object?> cells)
  {
   writer!.WriteStartElement(new Row());
   foreach(var cell in cells)
    if(cell is double number)writer.WriteElement(new Cell{DataType=CellValues.Number,CellValue=new CellValue(number)});
    else writer.WriteElement(new Cell{DataType=CellValues.InlineString,InlineString=new InlineString(new Text(Clean(cell?.ToString()??"")){Space=SpaceProcessingModeValues.Preserve})});
   writer.WriteEndElement();line++;
  }
  void Open(){var part=workbook.AddNewPart<WorksheetPart>();sheets.Add(workbook.GetIdOfPart(part));writer=OpenXmlWriter.Create(part);writer.WriteStartElement(new Worksheet());writer.WriteStartElement(new SheetData());line=0;Row(headers);}
  void Close(){writer!.WriteEndElement();writer.WriteEndElement();writer.Close();writer.Dispose();}
  foreach(var row in rows)
  {
   ct.ThrowIfCancellationRequested();
   if(writer==null||line>=MaximumSheetRows){if(writer!=null)Close();Open();}
   var cells=Cells(row).Cast<object?>().ToArray();if(row.Value.HasValue)cells[5]=row.Value.Value;Row(cells);
  }
  if(writer==null)Open();
  Close();
  workbook.Workbook=new Workbook(new Sheets(sheets.Select((id,i)=>new Sheet{Id=id,SheetId=(uint)(i+1),Name=sheets.Count==1&&line<=1?"History":"History "+(i+1)})));
  workbook.Workbook.Save();
 }
 public const string FormatChangeQuestion="RECORD FORMAT CHANGE: this PLC already has imported history in a different record format. Saving keeps every imported sample unchanged and imports only records with a newer sequence in the new format. Do this only after the PLC program was changed and its count and slot (D3903, D3904, D4903, D4904) were reset. Continue?";
 // V4.3.9: a record-format change (record size, fields or channels) is allowed once confirmed. Imported samples
 // are never re-read: the server records the highest imported sequence and new imports start above it.
 public static async Task<IResult> SaveMapping(ServerState s,Guid id,HistoryMappingInput input)
 {
  await s.Gate.WaitAsync();
  try
  {
   if(input.Revision!=s.Revision)return Results.Conflict(new{error="Configuration changed. Reload before saving."});
   var c=s.Clone();var controller=c.Controllers.SingleOrDefault(x=>x.Id==id);if(controller==null)return Results.NotFound();
   if(input.Layout is null)return Results.BadRequest(new{error="Enter the history layout."});
   long stored,newest;
   using(var db=s.Store.Open()){using var q=db.CreateCommand();q.CommandText="SELECT COUNT(*),COALESCE(MAX(sequence),0) FROM plc_snapshots WHERE controller=$id";q.Parameters.AddWithValue("$id",id.ToString());using var r=q.ExecuteReader();r.Read();stored=r.GetInt64(0);newest=r.GetInt64(1);}
   var action="PLC history mapping updated";
   input.Layout.ImportAfterSequence=controller.History.ImportAfterSequence; // server-owned, never taken from the browser
   if(stored>0&&!PlcHistory.SameRecordFormat(controller.History,input.Layout))
   {
    if(!PlcHistory.SameClock(controller.History,input.Layout))return Results.BadRequest(new{error="This PLC already has imported history. Clock settings (utcOffsetMinutes, timeZoneId, yearBase) cannot change here; use Correct stored timestamps."});
    if(!input.ConfirmFormatChange)return Results.Conflict(new{error=FormatChangeQuestion,confirmFormatChange=true});
    input.Layout.ImportAfterSequence=Math.Max(controller.History.ImportAfterSequence,newest);
    action=$"PLC history record format changed ({controller.History.RecordWords} -> {input.Layout.RecordWords} words, {controller.History.Channels.Count} -> {input.Layout.Channels.Count} channel(s)); {stored} imported record(s) kept; new imports start after sequence {input.Layout.ImportAfterSequence}";
   }
   controller.History=input.Layout;PlcHistory.Validate(controller,c);await s.Save(c,action);
   return Results.Ok(new{message=action});
  }
  catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
  finally{s.Gate.Release();}
 }
 static string Csv(string s)=>"\""+((s.Length>0&&"=+-@\t\r".Contains(s[0])&&!double.TryParse(s,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out _))?"'"+s:s).Replace("\"","\"\"")+"\"";
 static string[] Cells(StoredHistory r)=>[r.Controller.ToString(),r.Plc,r.Sensor.ToString(),r.Name,r.At,r.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"",r.Quality,r.Sequence?.ToString()??"",r.Source];
 public record StoredHistory(Guid Controller,string Plc,Guid Sensor,string Name,string At,double? Value,string Quality,long? Sequence,string Source);
 public static IEnumerable<StoredHistory> Rows(ServerState s,Guid? controller,Guid? sensor,DateTimeOffset? from,DateTimeOffset? to)
 {
  using var db=s.Store.Open();using var q=db.CreateCommand();
  q.CommandText="SELECT controller,sensor,at,value,quality,sequence,'PLC' FROM plc_history_values WHERE ($0 IS NULL OR controller=$0) AND ($1 IS NULL OR sensor=$1) AND ($2 IS NULL OR at >= $2) AND ($3 IS NULL OR at <= $3) UNION ALL SELECT NULL,sensor,at,value,quality,NULL,'Watchdog' FROM samples WHERE ($1 IS NULL OR sensor=$1) AND ($2 IS NULL OR at >= $2) AND ($3 IS NULL OR at <= $3) ORDER BY at,sensor,sequence";
  q.Parameters.AddWithValue("$0",(object?)controller?.ToString()??DBNull.Value);q.Parameters.AddWithValue("$1",(object?)sensor?.ToString()??DBNull.Value);q.Parameters.AddWithValue("$2",from.HasValue?Store.Utc(from.Value):DBNull.Value);q.Parameters.AddWithValue("$3",to.HasValue?Store.Utc(to.Value):DBNull.Value);
  var config=s.Engine.Config;var archived=DeletionEndpoints.Archived(s.Store);using var r=q.ExecuteReader();while(r.Read()){
   var sid=Guid.Parse(r.GetString(1));var sn=config.Sensors.FirstOrDefault(x=>x.Id==sid);var old=archived.GetValueOrDefault(sid);var cid=r.IsDBNull(0)?sn?.ControllerId??old.Controller:Guid.Parse(r.GetString(0));if(controller.HasValue&&cid!=controller)continue;
   yield return new(cid,config.Controllers.FirstOrDefault(x=>x.Id==cid)?.Name??old.Plc??cid.ToString(),sid,sn?.Name??old.Name??sid.ToString(),r.GetString(2),r.IsDBNull(3)?null:r.GetDouble(3),r.GetString(4),r.IsDBNull(5)?null:r.GetInt64(5),r.GetString(6));
  }
 }
 static object[] Gaps(Store store){using var db=store.Open();using var q=db.CreateCommand();q.CommandText="SELECT controller,first_sequence,last_sequence,detected FROM plc_gaps ORDER BY detected DESC LIMIT 100";using var r=q.ExecuteReader();var rows=new List<object>();while(r.Read())rows.Add(new{controller=r.GetString(0),first=r.GetInt64(1),last=r.GetInt64(2),detected=r.GetString(3)});return rows.ToArray();}
}
