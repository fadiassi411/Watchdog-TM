using Watchdog.TM;
public static class SmtpEndpoints
{
    public static void MapSmtp(this RouteGroupBuilder api)
    {
        api.MapGet("/smtp",async(ServerState s)=>{await s.Gate.WaitAsync();try{var c=s.Engine.Config;var m=c.Settings;return Results.Ok(new{revision=s.Revision,enabled=m.MailEnabled,automatic=m.AutomaticAlerts,host=m.SmtpHost,port=m.SmtpPort,security=m.MailSecurity,username=m.SmtpUsername,hasPassword=m.MailPassword.Length>0,senderEmail=m.SenderEmail,senderName=m.SenderName,testRecipient=m.TestRecipient,message=m.StandardMessage,simulation=m.Simulation,fault=s.MailFault,subscriptions=c.Sensors.Select(x=>new{x.Id,x.Name,enabled=x.EmailEnabled,recipients=x.Recipients,high=x.EmailHigh,low=x.EmailLow,recovery=x.RecoveryEmail,reminders=x.Reminders,reminderMinutes=x.ReminderMinutes}),history=s.Notifications.History()});}finally{s.Gate.Release();}});
        api.MapPost("/smtp",(ServerState s,MailInput input,HttpContext ctx)=>Save(s,input,false,ctx.RequestAborted));
        api.MapPost("/smtp/test",(ServerState s,MailInput input,HttpContext ctx)=>Save(s,input,true,ctx.RequestAborted));
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
