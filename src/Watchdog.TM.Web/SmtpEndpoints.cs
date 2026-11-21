using Watchdog.TM;
public static class SmtpEndpoints
{
    public static void MapSmtp(this RouteGroupBuilder api)
    {
        api.MapGet("/smtp",async(ServerState s,HttpContext ctx)=>{await s.Gate.WaitAsync();try{var who=Access.Current(ctx);var c=s.Engine.Config;var m=c.Settings;return Results.Ok(new{revision=s.Revision,enabled=m.MailEnabled,automatic=m.AutomaticAlerts,host=m.SmtpHost,port=m.SmtpPort,security=m.MailSecurity,username=m.SmtpUsername,hasPassword=m.MailPassword.Length>0,senderEmail=m.SenderEmail,senderName=m.SenderName,testRecipient=m.TestRecipient,message=m.StandardMessage,simulation=m.Simulation,fault=s.MailFault,readiness=AlarmNotifications.GlobalIssues(c),subscriptions=c.Sensors.Where(x=>who.Sees(x.Id)).Select(x=>new{x.Id,x.Name,enabled=x.EmailEnabled,recipients=x.Recipients,high=x.EmailHigh,low=x.EmailLow,recovery=x.RecoveryEmail,reminders=x.Reminders,reminderMinutes=x.ReminderMinutes,issues=AlarmNotifications.SensorIssues(c,x),coverage=AlarmNotifications.Coverage(x)}),history=s.Notifications.History()});}finally{s.Gate.Release();}});
        api.MapPost("/smtp",(ServerState s,MailInput input,HttpContext ctx)=>Save(s,input,false,ctx.RequestAborted));
        api.MapPost("/smtp/test",(ServerState s,MailInput input,HttpContext ctx)=>Save(s,input,true,ctx.RequestAborted));
        api.MapPost("/smtp/sensor/{id:guid}/test",(Guid id,ServerState s,HttpContext ctx)=>SensorTest(s,id,ctx.RequestAborted));
        api.MapPost("/smtp/sensor/{id:guid}",async(Guid id,SubscriptionInput input,ServerState s)=>{
            await s.Gate.WaitAsync();try{
                if(input.Revision!=s.Revision)return Results.Conflict(new{error="Settings changed. Reload this page and retry."});
                if(input.ReminderMinutes<1||input.ReminderMinutes>10080)return Results.BadRequest(new{error="Choose reminder spacing from 1 to 10080 minutes."});
                if(input.Enabled){SmtpDelivery.Addresses(input.Recipients);if(!input.High&&!input.Low)return Results.BadRequest(new{error="Select High, Low or both for this sensor."});}
                var c=s.Clone();var sensor=c.Sensors.FirstOrDefault(x=>x.Id==id);if(sensor==null)return Results.NotFound();
                sensor.EmailEnabled=input.Enabled;sensor.Recipients=input.Recipients.Trim();sensor.EmailHigh=input.High;sensor.EmailLow=input.Low;sensor.RecoveryEmail=input.Recovery;sensor.Reminders=input.Reminders;sensor.ReminderMinutes=input.ReminderMinutes;
                await s.Save(c,"Sensor email notification settings updated");s.Notifications.Reset(id);return Results.Ok(new{message="Sensor notification settings saved."});
            }catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}finally{s.Gate.Release();}
        });
    }
    // V4.4.1: sends one sample alarm email to the sensor's own recipients, using the saved SMTP settings and the
    // real alarm format. Nothing is queued or saved; the configuration lock is not held while sending.
    public static async Task<IResult> SensorTest(ServerState s,Guid id,CancellationToken ct)
    {
        var c=s.Engine.Config;var sensor=c.Sensors.FirstOrDefault(x=>x.Id==id);if(sensor is null)return Results.NotFound();
        if(string.IsNullOrWhiteSpace(sensor.Recipients))return Results.BadRequest(new{error="Save recipients for this sensor first."});
        try{SmtpDelivery.Addresses(sensor.Recipients);SmtpDelivery.Validate(c.Settings);}catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}
        var controller=c.Controllers.FirstOrDefault(x=>x.Id==sensor.ControllerId);var reading=s.Engine.Display(sensor);
        var body=$"{c.Settings.StandardMessage}\n\nTEST - this is a sample of the alarm email for this sensor. It is not a temperature alarm.\nWatchdog TM - {c.Settings.Site}\nSensor: {sensor.Name}\nLocation: {sensor.Location}\nController: {controller?.Name}\nCurrent reading: {(reading.Temperature is double t?t.ToString("F"+sensor.Decimals)+" °C":reading.Quality)}\nHigh limit: {sensor.SoftwareHighLimit?.ToString()??"Off"} °C\nLow limit: {sensor.SoftwareLowLimit?.ToString()??"Off"} °C\nSent: {DateTimeOffset.UtcNow:O}\nNo values were written to the PLC.";
        try{await SmtpDelivery.Send(c.Settings,sensor.Recipients,$"Watchdog TM - TEST alarm email - {sensor.Name}",body,Guid.NewGuid()+"@watchdog.local",ct);}
        catch(Exception ex) when(ex is not ArgumentException){return Results.BadRequest(new{error=SmtpDelivery.SafeError(ex)});}
        s.Store.Audit(Actor.Current,$"Test alarm email submitted for sensor {sensor.Id} (recipients omitted from audit)");
        var issues=AlarmNotifications.GlobalIssues(c).Concat(AlarmNotifications.SensorIssues(c,sensor)).ToArray();
        return Results.Ok(new{message=$"Test alarm email submitted to the mail server for {sensor.Recipients}."+(issues.Length>0?" Note: automatic alarm email for this sensor is still blocked: "+string.Join(" ",issues):" Automatic alarm email for this sensor is ready.")});
    }
    static async Task<IResult> Save(ServerState s,MailInput input,bool test,CancellationToken ct)
    {
        await s.Gate.WaitAsync(ct);try{
            if(input.Revision!=s.Revision)return Results.Conflict(new{error="Settings changed. Reload this page and retry."});
            var c=s.Clone();var m=c.Settings;
            if(input.Password?.Length>4096)return Results.BadRequest(new{error="SMTP password is too long."});
            if(input.ClearPassword&&!string.IsNullOrEmpty(input.Password))return Results.BadRequest(new{error="Choose either a new password or clear saved password."});
            if(m.MailPassword.Length>0&&string.IsNullOrEmpty(input.Password)&&!input.ClearPassword&&(m.SmtpHost!=input.Host.Trim()||m.SmtpUsername!=input.Username.Trim()))return Results.BadRequest(new{error="Re-enter the SMTP password when changing the server or username."});
            m.MailEnabled=input.Enabled;m.AutomaticAlerts=input.Automatic;m.SmtpHost=input.Host.Trim();m.SmtpPort=input.Port;m.MailSecurity=input.Security;m.SmtpUsername=input.Username.Trim();m.SenderEmail=input.SenderEmail.Trim();m.SenderName=input.SenderName.Trim();m.TestRecipient=input.TestRecipient.Trim();m.StandardMessage=input.Message;
            if(input.ClearPassword)m.MailPassword="";else if(!string.IsNullOrEmpty(input.Password))m.MailPassword=SmtpDelivery.Protect(input.Password);
            if(test||m.MailEnabled||m.SmtpHost.Length>0)SmtpDelivery.Validate(m);
            if(test){SmtpDelivery.Addresses(m.TestRecipient);try{await SmtpDelivery.Send(m,m.TestRecipient,"Watchdog TM - SMTP test",m.StandardMessage+"\n\nThis is a manually requested SMTP test from Watchdog TM. It is not a temperature alarm.\nSent: "+DateTimeOffset.UtcNow.ToString("O"),Guid.NewGuid()+"@watchdog.local",ct);}catch(Exception ex){return Results.BadRequest(new{error=SmtpDelivery.SafeError(ex)+" Settings were not saved."});}}
            try{await s.Save(c,test?"SMTP test submitted; settings saved (password omitted from audit)":"SMTP settings updated (password omitted from audit)");s.Notifications.Reset();s.MailFault="";}
            catch{return Results.BadRequest(new{error=test?"Test was submitted, but saving settings failed. Do not repeat the test solely to save; check server storage.":"Could not save SMTP settings. Check server storage."});}
            return Results.Ok(new{message=test?"Test submitted to the mail server. Settings saved. Inbox delivery is not guaranteed; check the recipient mailbox and spam folder.":"SMTP settings saved. Pending notifications were cancelled; active alarms are evaluated again on the next reading."});
        }catch(ArgumentException ex){return Results.BadRequest(new{error=ex.Message});}finally{s.Gate.Release();}
    }
}
