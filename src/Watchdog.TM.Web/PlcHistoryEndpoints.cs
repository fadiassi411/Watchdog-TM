using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Watchdog.TM;
public record HistoryMappingInput(string Revision,PlcHistoryLayout Layout);
public static class PlcHistoryEndpoints
{
 public static void MapPlcHistory(this RouteGroupBuilder api)
 {
  api.MapGet("/plc-history",(ServerState s,PlcHistoryWorker worker)=>Results.Ok(new{revision=s.Revision,controllers=s.Engine.Config.Controllers.Select(c=>new{c.Id,c.Name,c.History,progress=worker.Progress.GetValueOrDefault(c.Id)}),sensors=s.Engine.Config.Sensors.Select(x=>new{x.Id,x.Name,x.ControllerId}),gaps=Gaps(s.Store)}));
  api.MapPost("/plc-history/{id:guid}/mapping",async(Guid id,HistoryMappingInput input,ServerState s)=>{await s.Gate.WaitAsync();try{if(input.Revision!=s.Revision)return Results.Conflict(new{error="Configuration changed. Reload before saving."});var c=s.Clone();var controller=c.Controllers.SingleOrDefault(x=>x.Id==id);if(controller==null)return Results.NotFound();using(var db=s.Store.Open()){using var q=db.CreateCommand();q.CommandText="SELECT COUNT(*) FROM plc_snapshots WHERE controller=$id";q.Parameters.AddWithValue("$id",id.ToString());if(Convert.ToInt64(q.ExecuteScalar())>0){var old=JsonSerializer.SerializeToNode(controller.History)!;var next=JsonSerializer.SerializeToNode(input.Layout)!;foreach(var key in new[]{"Enabled","SyncSeconds","Verification"}){old.AsObject().Remove(key);next.AsObject().Remove(key);}if(old.ToJsonString()!=next.ToJsonString())return Results.BadRequest(new{error="This PLC already has imported history. Layout changes require a reviewed migration; existing sample identities must be preserved."});}}controller.History=input.Layout;PlcHistory.Validate(controller,c);await s.Save(c,"PLC history mapping updated");return Results.Ok();}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}finally{s.Gate.Release();}});
  api.MapPost("/plc-history/{id:guid}/download",(Guid id,ServerState s,PlcHistoryWorker worker)=>{var c=s.Engine.Config.Controllers.SingleOrDefault(x=>x.Id==id);if(c==null||!c.Enabled||!c.History.Enabled||s.Engine.Config.Settings.Simulation)return Results.BadRequest(new{error="Enable a verified history layout on a live controller first."});worker.Request(id);return Results.Ok(new{message="Download queued for this PLC."});});
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
    using var book=new XLWorkbook();int sheet=0,line=1;IXLWorksheet? ws=null;
    foreach(var row in Rows(s,controller,sensor,from,to)){
     ctx.RequestAborted.ThrowIfCancellationRequested();if(ws==null||line>1048576){ws=book.AddWorksheet("History "+(++sheet));for(int i=0;i<headers.Length;i++)ws.Cell(1,i+1).Value=headers[i];line=2;}
     var cells=Cells(row);for(int i=0;i<cells.Length;i++)ws.Cell(line,i+1).Value=cells[i];if(row.Value.HasValue)ws.Cell(line,6).Value=row.Value.Value;line++;
    }
    if(ws==null){ws=book.AddWorksheet("History");for(int i=0;i<headers.Length;i++)ws.Cell(1,i+1).Value=headers[i];}
    using var bytes=new MemoryStream();book.SaveAs(bytes);ctx.Response.ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";ctx.Response.Headers.ContentDisposition="attachment; filename=Watchdog-stored-history.xlsx";bytes.Position=0;await bytes.CopyToAsync(ctx.Response.Body,ctx.RequestAborted);
   }
  });
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
