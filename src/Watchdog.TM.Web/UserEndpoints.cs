using Watchdog.TM;

public record LevelInput(string Revision,Guid Id,string Name,string[] Permissions,bool AllSensors,Guid[] Sensors);
public record UserInput(string Revision,Guid Id,string Name,Guid LevelId,bool Enabled,string? Password);
public record RevisionInput(string Revision);

// V4.5.0: Administrator-only management of user levels (permission checkboxes) and user accounts.
public static class UserEndpoints
{
    public static void MapUsers(this RouteGroupBuilder api)
    {
        api.MapGet("/users",(ServerState s)=>{var c=s.Engine.Config;return Results.Ok(new{
            revision=s.Revision,catalog=Access.Catalog,
            levels=c.Settings.UserLevels,
            users=c.Settings.Users.Select(x=>new{x.Id,x.Name,x.LevelId,x.Enabled}),
            sensors=c.Sensors.Where(x=>!x.Retired).Select(x=>new{x.Id,x.Name,x.ControllerId,controller=c.Controllers.FirstOrDefault(p=>p.Id==x.ControllerId)?.Name})});});
        api.MapPost("/users/level",(LevelInput input,ServerState s)=>SaveLevel(s,input));
        api.MapPost("/users/level/{id:guid}/delete",(Guid id,RevisionInput input,ServerState s)=>DeleteLevel(s,id,input.Revision));
        api.MapPost("/users/user",(UserInput input,ServerState s)=>SaveUser(s,input));
        api.MapPost("/users/user/{id:guid}/delete",(Guid id,RevisionInput input,ServerState s)=>DeleteUser(s,id,input.Revision));
    }
    static IResult Changed()=>Results.Conflict(new{error="Settings changed in another browser. Reload and retry."});
    static string CleanName(string? name,string what)
    {
        var n=(name??"").Trim();
        if(n.Length is 0 or > 64||n.Any(char.IsControl))throw new ArgumentException($"Enter a {what} name of 1 to 64 characters.");
        return n;
    }
    public static async Task<IResult> SaveLevel(ServerState s,LevelInput input)
    {
        await s.Gate.WaitAsync();try{
            if(input.Revision!=s.Revision)return Changed();
            var name=CleanName(input.Name,"level");
            var unknown=(input.Permissions??[]).FirstOrDefault(x=>!Access.Known(x));if(unknown!=null)return Results.BadRequest(new{error=$"Unknown permission '{unknown}'."});
            var c=s.Clone();var levels=c.Settings.UserLevels;
            if(levels.Any(x=>x.Id!=input.Id&&x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))return Results.BadRequest(new{error="Another level already uses this name."});
            var sensors=(input.Sensors??[]).Distinct().ToList();
            if(sensors.Any(id=>!c.Sensors.Any(x=>x.Id==id)))return Results.BadRequest(new{error="A selected sensor no longer exists. Reload and retry."});
            if(!input.AllSensors&&sensors.Count==0)return Results.BadRequest(new{error="Select at least one sensor, or tick All sensors."});
            var level=levels.FirstOrDefault(x=>x.Id==input.Id);
            if(level is null){if(input.Id!=Guid.Empty)return Results.NotFound();level=new UserLevel();levels.Add(level);}
            level.Name=name;level.Permissions=Access.Catalog.Select(x=>x.Id).Where(x=>input.Permissions!.Contains(x)).ToList();
            level.AllSensors=input.AllSensors;level.Sensors=input.AllSensors?[]:sensors;
            await s.Save(c,$"User level '{name}' saved: {(level.Permissions.Count==0?"view dashboard only":string.Join(", ",level.Permissions))}; sensors: {(level.AllSensors?"all":level.Sensors.Count+" selected")}");
            return Results.Ok(new{id=level.Id});
        }catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}finally{s.Gate.Release();}
    }
    public static async Task<IResult> DeleteLevel(ServerState s,Guid id,string revision)
    {
        await s.Gate.WaitAsync();try{
            if(revision!=s.Revision)return Changed();
            var c=s.Clone();var level=c.Settings.UserLevels.FirstOrDefault(x=>x.Id==id);if(level is null)return Results.NotFound();
            var used=c.Settings.Users.Count(x=>x.LevelId==id);
            if(used>0)return Results.BadRequest(new{error=$"{used} user(s) still use this level. Move them to another level first."});
            c.Settings.UserLevels.Remove(level);await s.Save(c,$"User level '{level.Name}' deleted");return Results.Ok();
        }finally{s.Gate.Release();}
    }
    public static async Task<IResult> SaveUser(ServerState s,UserInput input)
    {
        await s.Gate.WaitAsync();try{
            if(input.Revision!=s.Revision)return Changed();
            var name=CleanName(input.Name,"user");
            if(Access.IsAdministratorName(name))return Results.BadRequest(new{error="'Administrator' and 'admin' are reserved for the built-in administrator."});
            var c=s.Clone();var users=c.Settings.Users;
            if(users.Any(x=>x.Id!=input.Id&&x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))return Results.BadRequest(new{error="Another user already has this name."});
            if(!c.Settings.UserLevels.Any(x=>x.Id==input.LevelId))return Results.BadRequest(new{error="Choose a user level."});
            if(input.Password?.Length>256)return Results.BadRequest(new{error="Password is too long."});
            var user=users.FirstOrDefault(x=>x.Id==input.Id);var created=user is null;
            if(created){if(input.Id!=Guid.Empty)return Results.NotFound();if(string.IsNullOrEmpty(input.Password))return Results.BadRequest(new{error="Enter a password for the new user (at least 10 characters)."});user=new WebUser();}
            if(!string.IsNullOrEmpty(input.Password))user!.PasswordHash=Security.Hash(input.Password);
            user!.Name=name;user.LevelId=input.LevelId;user.Enabled=input.Enabled;
            if(created)users.Add(user);
            var level=c.Settings.UserLevels.First(x=>x.Id==input.LevelId).Name;
            await s.Save(c,$"User '{name}' {(created?"created":"updated")}: level '{level}', {(user.Enabled?"enabled":"disabled")}{(!created&&!string.IsNullOrEmpty(input.Password)?", password reset":"")} (password omitted from audit)");
            return Results.Ok(new{id=user.Id});
        }catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
        catch(Exception ex) when(ex.Message=="Use at least 10 characters."){return Results.BadRequest(new{error="Password: use at least 10 characters."});}
        finally{s.Gate.Release();}
    }
    public static async Task<IResult> DeleteUser(ServerState s,Guid id,string revision)
    {
        await s.Gate.WaitAsync();try{
            if(revision!=s.Revision)return Changed();
            var c=s.Clone();var user=c.Settings.Users.FirstOrDefault(x=>x.Id==id);if(user is null)return Results.NotFound();
            c.Settings.Users.Remove(user);await s.Save(c,$"User '{user.Name}' deleted; their sign-in ends immediately");return Results.Ok();
        }finally{s.Gate.Release();}
    }
}
