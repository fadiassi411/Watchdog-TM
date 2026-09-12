using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Watchdog.TM;

public sealed record MailInput(string Revision, bool Enabled, bool Automatic, string Host, int Port, string Security,
    string Username, string? Password, bool ClearPassword, string SenderEmail, string SenderName, string TestRecipient, string Message);
public sealed record SubscriptionInput(string Revision, bool Enabled, string Recipients, bool High, bool Low, bool Recovery, bool Reminders, int ReminderMinutes);
public static class SmtpDelivery
{
    public static string Protect(string value)=>Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.LocalMachine));
    public static string Unprotect(string value)=>value.Length==0?"":Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value),null,DataProtectionScope.LocalMachine));
    public static string[] Addresses(string value,bool required=true)
    {
        if(value.Length>4000)throw new ArgumentException("Recipient list is too long.");
        var result=value.Split([';',','],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if((required&&result.Length==0)||result.Length>25)throw new ArgumentException("Enter between 1 and 25 email addresses separated by semicolons.");
        foreach(var address in result)if(address.Contains('\r')||address.Contains('\n')||!MailboxAddress.TryParse(address,out var box)||box.Address!=address||!address.Contains('@'))throw new ArgumentException("Enter valid email addresses without display names.");
        return result;
    }
    public static void Validate(Settings s)
    {
        if(string.IsNullOrWhiteSpace(s.SmtpHost)||s.SmtpHost.Length>255||s.SmtpHost.Any(char.IsWhiteSpace)||s.SmtpHost.Contains('/')||s.SmtpPort<1||s.SmtpPort>65535)throw new ArgumentException("Enter an SMTP server name and port from 1 to 65535.");
        if(s.MailSecurity is not ("StartTls" or "SslOnConnect" or "None"))throw new ArgumentException("Choose STARTTLS, SSL/TLS, or no encryption.");
        Addresses(s.SenderEmail);if(Addresses(s.SenderEmail).Length!=1)throw new ArgumentException("Enter one sender email address.");
        if(s.SenderName.Length>160||s.SenderName.Contains('\r')||s.SenderName.Contains('\n')||s.SmtpUsername.Length>320||s.StandardMessage.Length>8000)throw new ArgumentException("Sender or message fields exceed supported limits.");
        if(s.MailSecurity=="None"&&!string.IsNullOrWhiteSpace(s.SmtpUsername))throw new ArgumentException("Use TLS for SMTP authentication. Unencrypted mode supports a trusted relay without login only.");
        if(!string.IsNullOrWhiteSpace(s.SmtpUsername)&&string.IsNullOrEmpty(s.MailPassword))throw new ArgumentException("Enter an SMTP password or app password for this username.");
        if(!string.IsNullOrWhiteSpace(s.TestRecipient))Addresses(s.TestRecipient);
    }
    public static async Task Send(Settings s,string recipients,string subject,string body,string messageId,CancellationToken ct)
    {
        Validate(s);var msg=new MimeMessage();msg.From.Add(new MailboxAddress(s.SenderName,s.SenderEmail));
        // Bcc protects recipients from disclosure to other recipients.
        foreach(var address in Addresses(recipients))msg.Bcc.Add(MailboxAddress.Parse(address));
        msg.Subject=subject;msg.MessageId=messageId;msg.Body=new TextPart("plain"){Text=body};
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var client=new SmtpClient{Timeout=10000};
        var security=s.MailSecurity=="StartTls"?SecureSocketOptions.StartTls:s.MailSecurity=="SslOnConnect"?SecureSocketOptions.SslOnConnect:SecureSocketOptions.None;
        await client.ConnectAsync(s.SmtpHost,s.SmtpPort,security,timeout.Token);
        if(!string.IsNullOrWhiteSpace(s.SmtpUsername))await client.AuthenticateAsync(s.SmtpUsername,Unprotect(s.MailPassword),timeout.Token);
        await client.SendAsync(msg,timeout.Token);
        // After DATA acceptance the message was submitted, even if QUIT fails.
        try{await client.DisconnectAsync(true,timeout.Token);}catch{}
    }
    public static string SafeError(Exception ex)=>ex is MailKit.Security.AuthenticationException?"SMTP authentication failed. Check username and app password.":ex is SslHandshakeException?"TLS validation failed. Check the server, port, certificate and Windows clock.":ex is OperationCanceledException?"SMTP operation timed out or was cancelled. Submission may be uncertain.":"SMTP submission failed or is uncertain. Check the server, port, TLS mode, sender permission and connection. A retry can duplicate mail accepted before a connection loss.";
}

public sealed class AlarmNotifications
{
    readonly Store store;
    public AlarmNotifications(Store store){this.store=store;Ensure();}
    public void Ensure(){store.Write(db=>Store.Run(db,"""
        CREATE TABLE IF NOT EXISTS mail_alarm_state(sensor TEXT PRIMARY KEY, level TEXT NOT NULL, episode TEXT NOT NULL, observed TEXT NOT NULL, reminder TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS mail_outbox(id INTEGER PRIMARY KEY, sensor TEXT NOT NULL, episode TEXT NOT NULL, kind TEXT NOT NULL, recipients TEXT NOT NULL, subject TEXT NOT NULL, body TEXT NOT NULL, message_id TEXT NOT NULL UNIQUE, status TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0, next TEXT NOT NULL, error TEXT NOT NULL DEFAULT '',created TEXT NOT NULL);
        """));}
    public static bool Eligible(Configuration c,Sensor s)=>!c.Settings.Simulation&&c.Settings.MailEnabled&&c.Settings.AutomaticAlerts&&s.EmailEnabled&&s.Enabled&&!s.Retired&&c.Settings.CommissionedSensors.Contains(s.Id)&&c.Controllers.Any(x=>x.Id==s.ControllerId&&x.Enabled&&x.Protocol!=Protocol.Simulation);
    public void Reset(Guid? id=null){store.Write(db=>{if(id is {} sensor){Store.Run(db,"DELETE FROM mail_alarm_state WHERE sensor=$0",sensor.ToString());Store.Run(db,"UPDATE mail_outbox SET status='Cancelled',error='Notification configuration changed' WHERE sensor=$0 AND status='Pending'",sensor.ToString());}else{Store.Run(db,"DELETE FROM mail_alarm_state");Store.Run(db,"UPDATE mail_outbox SET status='Cancelled',error='Notification configuration changed' WHERE status='Pending'");}});}
    public void Observe(Configuration c,Sensor s,Reading r)
    {
        if(!Eligible(c,s))return;
        var level=SoftwareAlarms.Evaluate(s,r);if(level=="Unavailable")return; // An outage is not a recovery.
        bool Subscribed(string value)=>value=="High"?s.EmailHigh:value=="Low"&&s.EmailLow;
        store.Write(db=>{
            using var q=db.CreateCommand();
            q.CommandText="SELECT level,episode,observed,reminder FROM mail_alarm_state WHERE sensor=$id";q.Parameters.AddWithValue("$id",s.Id.ToString());
            string old="Normal",episode="",observed="",reminder="";using(var reader=q.ExecuteReader())if(reader.Read()){old=reader.GetString(0);episode=reader.GetString(1);observed=reader.GetString(2);reminder=reader.GetString(3);}
            void Run(string sql,params object[] values){using var cmd=db.CreateCommand();cmd.CommandText=sql;for(int i=0;i<values.Length;i++)cmd.Parameters.AddWithValue("$"+i,values[i]);cmd.ExecuteNonQuery();}
            void Queue(string kind,string ep,string first){
                var controller=c.Controllers.First(x=>x.Id==s.ControllerId);
                var body=$"{c.Settings.StandardMessage}\n\nWatchdog TM - {c.Settings.Site}\nEvent: {kind}\nSensor: {s.Name}\nLocation: {s.Location}\nController: {controller.Name}\nObserved reading: {r.Temperature?.ToString("F"+s.Decimals)} °C\nHigh limit: {s.SoftwareHighLimit?.ToString()??"Off"} °C\nLow limit: {s.SoftwareLowLimit?.ToString()??"Off"} °C\nEvent observed: {r.At:O}\nEpisode first observed: {first}\nThis notification describes the observation at the stated time; check the dashboard for the current condition.\nNo values were written to the PLC.";
                Run("INSERT INTO mail_outbox(sensor,episode,kind,recipients,subject,body,message_id,status,next,created) VALUES($0,$1,$2,$3,$4,$5,$6,'Pending',$7,$7)",s.Id.ToString(),ep,kind,s.Recipients,$"Watchdog TM - {kind} - {s.Name}",body,Guid.NewGuid()+"@watchdog.local",Store.Utc(r.At));
            }
            if(level!=old){
                Run("UPDATE mail_outbox SET status='Cancelled',error='Alarm condition ended' WHERE episode=$0 AND kind LIKE '%reminder' AND status='Pending'",episode);
                if(old!="Normal"&&Subscribed(old)&&level=="Normal"&&s.RecoveryEmail)Queue("Return to normal",episode,observed);
                episode=Guid.NewGuid().ToString();observed=Store.Utc(r.At);reminder=observed;
                if(Subscribed(level))Queue(level+" temperature",episode,observed);
            }else if(Subscribed(level)&&s.Reminders&&DateTimeOffset.TryParse(reminder,out var last)&&r.At-last>=TimeSpan.FromMinutes(s.ReminderMinutes)){
                Queue(level+" reminder",episode,observed);reminder=Store.Utc(r.At);
            }
            Run("INSERT INTO mail_alarm_state(sensor,level,episode,observed,reminder) VALUES($0,$1,$2,$3,$4) ON CONFLICT(sensor) DO UPDATE SET level=excluded.level,episode=excluded.episode,observed=excluded.observed,reminder=excluded.reminder",s.Id.ToString(),level,episode,observed,reminder);
        });
    }
    public async Task ProcessOne(Configuration c,Func<Settings,string,string,string,string,CancellationToken,Task> send,CancellationToken ct)
    {
        if(c.Settings.Simulation||!c.Settings.MailEnabled||!c.Settings.AutomaticAlerts)return;
        long id;Guid sensorId;string kind,recipients,subject,body,messageId;int attempts;
        using(var db=store.Open()){using var q=db.CreateCommand();q.CommandText="SELECT id,sensor,kind,recipients,subject,body,message_id,attempts FROM mail_outbox WHERE status='Pending' AND next<=$now ORDER BY id LIMIT 1";q.Parameters.AddWithValue("$now",Store.Utc(DateTimeOffset.UtcNow));using var r=q.ExecuteReader();if(!r.Read())return;id=r.GetInt64(0);sensorId=Guid.Parse(r.GetString(1));kind=r.GetString(2);recipients=r.GetString(3);subject=r.GetString(4);body=r.GetString(5);messageId=r.GetString(6);attempts=r.GetInt32(7);}
        var sensor=c.Sensors.FirstOrDefault(x=>x.Id==sensorId);
        if(sensor==null||!Eligible(c,sensor)||sensor.Recipients!=recipients){store.Write(db=>Store.Run(db,"UPDATE mail_outbox SET status='Cancelled',error='Sensor notifications disabled or recipients changed' WHERE id=$0",id));return;}
        try{await send(c.Settings,recipients,subject,body,messageId,ct);store.Write(db=>Store.Run(db,"UPDATE mail_outbox SET status='Submitted to mail server',attempts=attempts+1,error='' WHERE id=$0",id));}
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex){store.Write(db=>Store.Run(db,"UPDATE mail_outbox SET status=$0,attempts=attempts+1,next=$1,error=$2 WHERE id=$3",attempts>=4?"Failed":"Pending",Store.Utc(DateTimeOffset.UtcNow.AddSeconds(Math.Min(1800,30*Math.Pow(2,attempts)))),SmtpDelivery.SafeError(ex),id));}
    }
    public object[] History(){using var db=store.Open();using var q=db.CreateCommand();q.CommandText="SELECT id,sensor,kind,status,attempts,created,error FROM mail_outbox ORDER BY id DESC LIMIT 100";using var r=q.ExecuteReader();var list=new List<object>();while(r.Read())list.Add(new{id=r.GetInt64(0),sensor=r.GetString(1),kind=r.GetString(2),status=r.GetString(3),attempts=r.GetInt32(4),created=r.GetString(5),error=r.GetString(6)});return list.ToArray();}
}

public sealed class NotificationWorker(ServerState state):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested){await state.Gate.WaitAsync(ct);try{await state.Notifications.ProcessOne(state.Engine.Config,SmtpDelivery.Send,ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){return;}catch{state.MailFault="Email queue could not be processed. Check server storage and SMTP settings.";}finally{state.Gate.Release();}await Task.Delay(2000,ct);}
    }
}
