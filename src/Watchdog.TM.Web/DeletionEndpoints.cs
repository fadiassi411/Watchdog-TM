using Watchdog.TM;

public record DeleteInput(string Revision);
public static class DeletionEndpoints
{
    public static void MapDeletion(this RouteGroupBuilder api)
    {
        api.MapPost("/controller/{id:guid}/delete", (Guid id, DeleteInput input, ServerState s) => Delete(s,id,true,input));
        api.MapPost("/sensor/{id:guid}/delete", (Guid id, DeleteInput input, ServerState s) => Delete(s,id,false,input));
    }

    public static async Task<IResult> Delete(ServerState s, Guid id, bool controller, DeleteInput input)
    {
        await s.Gate.WaitAsync();
        try
        {
            if(input.Revision!=s.Revision) return Results.Conflict(new {error="Settings changed. Reload before deleting."});
            var c=s.Clone();
            if(controller ? !c.Controllers.Any(x=>x.Id==id) : !c.Sensors.Any(x=>x.Id==id)) return Results.NotFound();
            var removed=c.Sensors.Where(x=>controller ? x.ControllerId==id : x.Id==id).ToArray();
            var name=controller ? c.Controllers.Single(x=>x.Id==id).Name : c.Sensors.Single(x=>x.Id==id).Name;
            var backup="before-delete-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".db";
            for(var attempt=0;;attempt++)
            {
                try { s.Store.Backup(Path.Combine(s.DirectoryPath,backup)); break; }
                catch(Microsoft.Data.Sqlite.SqliteException ex) when(attempt<4 && ex.SqliteErrorCode is 5 or 6)
                { await Task.Delay(100*(attempt+1)); }
            }
            // Preserve names and ownership for exports after the active configuration is removed.
            s.Store.Write(db=>{
                Store.Run(db,"CREATE TABLE IF NOT EXISTS deleted_sensor_metadata(sensor TEXT PRIMARY KEY,controller TEXT NOT NULL,name TEXT NOT NULL,plc TEXT NOT NULL)");
                foreach(var sensor in removed) Store.Run(db,"INSERT OR REPLACE INTO deleted_sensor_metadata(sensor,controller,name,plc) VALUES($0,$1,$2,$3)",sensor.Id.ToString(),sensor.ControllerId.ToString(),sensor.Name,c.Controllers.Single(x=>x.Id==sensor.ControllerId).Name);
            });
            var ids=removed.Select(x=>x.Id).ToHashSet();
            c.Sensors.RemoveAll(x=>ids.Contains(x.Id));
            c.Settings.CommissionedSensors.RemoveAll(ids.Contains);
            if(controller) c.Controllers.RemoveAll(x=>x.Id==id);
            foreach(var plc in c.Controllers)
            {
                plc.History.Channels.RemoveAll(x=>ids.Contains(x.SensorId));
                if(plc.History.Channels.Count==0) plc.History.Enabled=false;
            }
            await s.Save(c,$"Deleted {(controller?"controller":"sensor/register")} '{name}' ({id}); removed {removed.Length} sensor(s); history retained; backup {backup}");
            foreach(var sensor in removed) s.Notifications.Reset(sensor.Id);
            return Results.Ok(new {message="Deleted. Saved readings remain in PLC history and combined trends; a backup was created.",backup});
        }
        finally { s.Gate.Release(); }
    }

    public static Dictionary<Guid,(Guid Controller,string Name,string Plc)> Archived(Store store)
    {
        using var db=store.Open();using var q=db.CreateCommand();
        q.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='deleted_sensor_metadata'";
        if(Convert.ToInt64(q.ExecuteScalar())==0)return new();
        q.CommandText="SELECT sensor,controller,name,plc FROM deleted_sensor_metadata";
        using var r=q.ExecuteReader();var result=new Dictionary<Guid,(Guid,string,string)>();
        while(r.Read())result[Guid.Parse(r.GetString(0))]=(Guid.Parse(r.GetString(1)),r.GetString(2),r.GetString(3));
        return result;
    }
}
