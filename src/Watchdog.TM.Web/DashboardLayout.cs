using Watchdog.TM;

public record DashboardOrderInput(string Revision,Guid[] Sensors,bool Reset=false);

// V4.6.0: the order of the temperature blocks on the dashboard, saved on the server so every screen shows the same layout.
public static class DashboardLayout
{
    // Saved order first; sensors never arranged (for example added later) follow in configuration order.
    public static List<Sensor> Ordered(Configuration c)
    {
        var rank=c.Settings.DashboardOrder.Select((id,i)=>(id,i)).GroupBy(x=>x.id).ToDictionary(g=>g.Key,g=>g.First().i);
        return c.Sensors.Select((s,i)=>(s,i)).OrderBy(x=>rank.TryGetValue(x.s.Id,out var r)?r:int.MaxValue).ThenBy(x=>x.i).Select(x=>x.s).ToList();
    }
    // Puts the visible sensors into the requested sequence while every sensor the user cannot see keeps its slot.
    public static List<Guid> Apply(Configuration c,IReadOnlyList<Guid> arranged,Func<Guid,bool> visible)
    {
        var full=Ordered(c).Select(x=>x.Id).ToList();
        var queue=new Queue<Guid>(arranged);
        for(var i=0;i<full.Count;i++) if(visible(full[i])&&arranged.Contains(full[i])) full[i]=queue.Dequeue();
        return full;
    }
    public static void MapDashboard(this RouteGroupBuilder api)
    {
        api.MapPost("/dashboard/order",(DashboardOrderInput input,ServerState s,HttpContext ctx)=>Save(s,Access.Current(ctx),input));
    }
    public static async Task<IResult> Save(ServerState s,Access.Principal who,DashboardOrderInput input)
    {
        await s.Gate.WaitAsync();try{
            if(input.Revision!=s.Revision)return Results.Conflict(new{error="Settings changed in another browser. Reload and retry."});
            var c=s.Clone();
            var shown=Ordered(c).Where(x=>!x.Retired&&who.Sees(x.Id)).Select(x=>x.Id).ToList();
            var requested=input.Reset?c.Sensors.Where(x=>shown.Contains(x.Id)).Select(x=>x.Id).ToList():(input.Sensors??[]).ToList();
            if(requested.Count!=shown.Count||requested.Distinct().Count()!=requested.Count||requested.Any(id=>!shown.Contains(id)))
                return Results.BadRequest(new{error="The dashboard changed while you were arranging it. Reload and arrange again."});
            var order=Apply(c,requested,id=>shown.Contains(id));
            // Store nothing when the result is the plain configuration order, so new sensors simply appear at the end.
            c.Settings.DashboardOrder=order.SequenceEqual(c.Sensors.Select(x=>x.Id))?[]:order;
            await s.Save(c,input.Reset?"Dashboard layout reset to the default order":"Dashboard layout arranged: "+string.Join(", ",requested.Select(id=>c.Sensors.First(x=>x.Id==id).Name)));
            return Results.Ok();
        }finally{s.Gate.Release();}
    }
}
