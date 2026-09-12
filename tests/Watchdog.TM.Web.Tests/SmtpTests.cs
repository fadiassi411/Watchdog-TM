using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Watchdog.TM;

internal static class SmtpTests
{
    public static async Task Run()
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new Exception("FAIL SMTP: "+label);checks++;Console.WriteLine("PASS SMTP "+label);}
        void Reject(Action action,string label){try{action();}catch(ArgumentException){Check(true,label);return;}throw new Exception("FAIL: "+label);}
        var settings=new Settings{SmtpHost="localhost",SmtpPort=25,MailSecurity="None",SenderEmail="watchdog@example.test",SenderName="Watchdog test"};
        Reject(()=>SmtpDelivery.Addresses(""),"empty recipients rejected");Reject(()=>SmtpDelivery.Addresses("invalid"),"invalid address rejected");Reject(()=>SmtpDelivery.Addresses("a@example.test\r\nBcc: b@example.test"),"header injection rejected");
        Check(SmtpDelivery.Addresses("a@example.test;a@example.test,b@example.test").Length==2,"recipient normalization and deduplication");
        settings.SmtpPort=0;Reject(()=>SmtpDelivery.Validate(settings),"invalid port rejected");settings.SmtpPort=25;
        settings.MailSecurity="Bogus";Reject(()=>SmtpDelivery.Validate(settings),"invalid TLS mode rejected");settings.MailSecurity="None";settings.SmtpUsername="user";Reject(()=>SmtpDelivery.Validate(settings),"unencrypted credentials blocked");settings.SmtpUsername="";
        const string secret="test-password-not-a-real-credential";var protectedValue=SmtpDelivery.Protect(secret);Check(protectedValue!=secret&&SmtpDelivery.Unprotect(protectedValue)==secret,"machine-encrypted password round trip");
        Check(!SmtpDelivery.SafeError(new Exception(secret)).Contains(secret),"SMTP errors do not expose credentials or server text");
        Check(SmtpDelivery.SafeError(new MailKit.Security.AuthenticationException("secret")).Contains("authentication"),"authentication errors are actionable");
        var root=Path.Combine(Environment.CurrentDirectory,"work","smtp-test-"+Guid.NewGuid());Directory.CreateDirectory(root);var store=new Store(Path.Combine(root,"test.db"));var n=new AlarmNotifications(store);
        var c=new Configuration();c.Settings.Simulation=false;c.Settings.MailEnabled=true;c.Settings.AutomaticAlerts=true;
        var controller=new Controller{Protocol=Protocol.TCP};c.Controllers.Add(controller);
        var sensor=new Sensor{ControllerId=controller.Id,TemperatureOffset=1,SoftwareHighLimit=8,SoftwareLowLimit=2,EmailEnabled=true,Recipients="one@example.test;two@example.test",RecoveryEmail=true,Reminders=true,ReminderMinutes=1};c.Sensors.Add(sensor);c.Settings.CommissionedSensors.Add(sensor.Id);
        JsonElement[] Rows()=>n.History().Select(x=>JsonSerializer.SerializeToElement(x)).ToArray();
        int Count(string kind)=>Rows().Count(x=>x.GetProperty("kind").GetString()==kind);
        var now=DateTimeOffset.UtcNow.AddMinutes(-10);
        void Read(double? value,int seconds,string quality="VALID")=>n.Observe(c,sensor,new(now.AddSeconds(seconds),value,null,null,null,false,quality));
        Read(9,0);Read(9,1);Check(Count("High temperature")==1,"initial high alarm queues once across polls");
        n=new AlarmNotifications(store);Read(9,2);Check(Count("High temperature")==1,"restart preserves episode deduplication");
        Read(null,70,"OFFLINE");Check(Count("Return to normal")==0&&Count("High reminder")==0,"outage neither recovers nor creates reminders");
        Read(9,75);Read(9,76);Check(Count("High reminder")==1,"fresh alarm reminder obeys spacing");
        Read(5,80);Check(Count("Return to normal")==1,"recovery queues on real normal reading");Check(Rows().Single(x=>x.GetProperty("kind").GetString()=="High reminder").GetProperty("status").GetString()=="Cancelled","recovery cancels pending reminders");
        Read(1,90);Read(1,91);Check(Count("Low temperature")==1,"low alarm queues once");
        var sent=new List<string>();await n.ProcessOne(c,(s,to,subject,body,id,ct)=>{sent.Add(subject);return Task.CompletedTask;},default);
        Check(sent.Count==1&&Rows().Any(x=>x.GetProperty("status").GetString()=="Submitted to mail server"),"successful dispatch marked submitted");
        n.Reset();Read(9,100);
        for(int i=0;i<5;i++){store.Write(db=>Store.Run(db,"UPDATE mail_outbox SET next=$0 WHERE status='Pending'",Store.Utc(DateTimeOffset.UtcNow.AddSeconds(-1))));await n.ProcessOne(c,(s,to,subject,body,id,ct)=>throw new IOException(secret),default);}
        Check(Rows().Any(x=>x.GetProperty("status").GetString()=="Failed"&&x.GetProperty("attempts").GetInt32()==5),"delivery retries stop after five attempts");
        Check(Rows().All(x=>!x.GetProperty("error").GetString()!.Contains(secret)),"delivery log uses sanitized errors");
        n.Reset();c.Settings.Simulation=true;Read(9,110);Check(!Rows().Any(x=>x.GetProperty("status").GetString()=="Pending"),"simulation prevents automatic queueing");c.Settings.Simulation=false;
        c.Settings.AutomaticAlerts=false;Read(9,120);Check(!Rows().Any(x=>x.GetProperty("status").GetString()=="Pending"),"automatic switch prevents queueing");c.Settings.AutomaticAlerts=true;
        c.Settings.MailEnabled=false;Read(9,121);Check(!Rows().Any(x=>x.GetProperty("status").GetString()=="Pending"),"global email switch prevents queueing");c.Settings.MailEnabled=true;
        sensor.Enabled=false;Read(9,122);Check(!Rows().Any(x=>x.GetProperty("status").GetString()=="Pending"),"disabled sensor suppressed");sensor.Enabled=true;
        sensor.EmailHigh=false;Read(9,125);Read(1,130);Check(Rows().Count(x=>x.GetProperty("status").GetString()=="Pending")==1,"per-sensor high off leaves low alert enabled");
        n.Reset();sensor.EmailHigh=true;Read(9,140);sensor.EmailEnabled=false;int before=sent.Count;await n.ProcessOne(c,(s,to,sub,b,id,ct)=>{sent.Add(sub);return Task.CompletedTask;},default);Check(sent.Count==before,"disabled recipient subscription cancels pending dispatch");sensor.EmailEnabled=true;
        // Real local SMTP protocol test. No internet recipients or delivery.
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();settings.SmtpPort=((IPEndPoint)listener.LocalEndpoint).Port;settings.SmtpHost="127.0.0.1";
        string captured="";var recipients=new List<string>();
        var server=Task.Run(async()=>{using var connection=await listener.AcceptTcpClientAsync();using var stream=connection.GetStream();using var reader=new StreamReader(stream,Encoding.UTF8);using var writer=new StreamWriter(stream,Encoding.ASCII){AutoFlush=true,NewLine="\r\n"};await writer.WriteLineAsync("220 localhost test SMTP");while(await reader.ReadLineAsync() is {} line){if(line.StartsWith("EHLO")){await writer.WriteLineAsync("250-localhost");await writer.WriteLineAsync("250 SIZE 1000000");}else if(line.StartsWith("MAIL"))await writer.WriteLineAsync("250 OK");else if(line.StartsWith("RCPT")){recipients.Add(line);await writer.WriteLineAsync("250 OK");}else if(line=="DATA"){await writer.WriteLineAsync("354 End with dot");var data=new StringBuilder();while(await reader.ReadLineAsync() is {} part&&part!=".")data.AppendLine(part);captured=data.ToString();await writer.WriteLineAsync("250 Accepted");}else if(line=="QUIT"){await writer.WriteLineAsync("221 Bye");break;}else await writer.WriteLineAsync("250 OK");}});
        await SmtpDelivery.Send(settings,"one@example.test;two@example.test","Test message","Test only body","fixed-id@watchdog.local",default);await server.WaitAsync(TimeSpan.FromSeconds(5));
        Check(recipients.Count==2,"real SMTP envelope has both recipients");Check(captured.Contains("Test only body")&&captured.Contains("fixed-id@watchdog.local"),"real SMTP contains message body and stable message ID");Check(!captured.Contains("one@example.test")&&!captured.Contains("two@example.test"),"Bcc recipients absent from message headers");
        // A relay without STARTTLS must never receive credentials or a message when TLS is required.
        var tlsServer=Task.Run(async()=>{using var connection=await listener.AcceptTcpClientAsync();using var stream=connection.GetStream();using var reader=new StreamReader(stream);using var writer=new StreamWriter(stream){AutoFlush=true,NewLine="\r\n"};await writer.WriteLineAsync("220 local");await reader.ReadLineAsync();await writer.WriteLineAsync("250 localhost");});
        settings.MailSecurity="StartTls";bool rejected=false;try{await SmtpDelivery.Send(settings,"one@example.test","Must not send","No plaintext fallback","tls-test@watchdog.local",default);}catch{rejected=true;}await tlsServer.WaitAsync(TimeSpan.FromSeconds(5));Check(rejected,"required STARTTLS refuses unsupported plaintext server");
        Console.WriteLine($"SMTP checks passed: {checks}");
    }
}
