using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Watchdog.TM;

// End-to-end check of the live alarm e-mail path, wired exactly like the Windows service:
// fake Modbus TCP PLC -> MonitorEngine polling -> software alarm -> mail queue -> NotificationWorker
// -> real SMTP client -> fake local SMTP server. No PLC writes, no internet delivery.
public static class AlarmEmailFlowTests
{
 public static async Task Run()
 {
  void Check(bool ok,string name){if(!ok)throw new Exception("FAIL alarm email: "+name);Console.WriteLine("PASS alarm email "+name);}

  // Fake PLC: answers FC03 for any address with the current raw value (tenths of a degree).
  int raw=200;
  var plc=new TcpListener(IPAddress.Loopback,0);plc.Start();int plcPort=((IPEndPoint)plc.LocalEndpoint).Port;
  using var stop=new CancellationTokenSource();
  _=Task.Run(async()=>{while(!stop.IsCancellationRequested){TcpClient client;try{client=await plc.AcceptTcpClientAsync(stop.Token);}catch{return;}
   _=Task.Run(async()=>{using(client){var s=client.GetStream();var head=new byte[12];try{while(true){int got=0;while(got<12){int n=await s.ReadAsync(head.AsMemory(got,12-got));if(n==0)return;got+=n;}
    int qty=(head[10]<<8)|head[11];var pdu=new byte[2+qty*2];pdu[0]=3;pdu[1]=(byte)(qty*2);for(int i=0;i<qty;i++){pdu[2+i*2]=(byte)(raw>>8);pdu[3+i*2]=(byte)raw;}
    int len=1+pdu.Length;var reply=new byte[]{head[0],head[1],0,0,(byte)(len>>8),(byte)len,head[6]}.Concat(pdu).ToArray();await s.WriteAsync(reply);}}catch{}}});}});

  // Fake SMTP server: records every accepted message.
  var mails=new List<(string Rcpt,string Data)>();
  var smtp=new TcpListener(IPAddress.Loopback,0);smtp.Start();int smtpPort=((IPEndPoint)smtp.LocalEndpoint).Port;
  _=Task.Run(async()=>{while(!stop.IsCancellationRequested){TcpClient client;try{client=await smtp.AcceptTcpClientAsync(stop.Token);}catch{return;}
   _=Task.Run(async()=>{using(client){var st=client.GetStream();var r=new StreamReader(st,Encoding.UTF8);var w=new StreamWriter(st,Encoding.ASCII){AutoFlush=true,NewLine="\r\n"};var rcpt=new StringBuilder();
    await w.WriteLineAsync("220 test");while(await r.ReadLineAsync() is {} line){if(line.StartsWith("EHLO")){await w.WriteLineAsync("250-test");await w.WriteLineAsync("250 SIZE 1000000");}
     else if(line.StartsWith("RCPT")){rcpt.Append(line);await w.WriteLineAsync("250 OK");}
     else if(line=="DATA"){await w.WriteLineAsync("354 go");var d=new StringBuilder();while(await r.ReadLineAsync() is {} p&&p!=".")d.AppendLine(p);lock(mails)mails.Add((rcpt.ToString(),d.ToString()));await w.WriteLineAsync("250 accepted");}
     else if(line=="QUIT"){await w.WriteLineAsync("221 bye");break;}else await w.WriteLineAsync("250 OK");}}});}});

  // Live configuration written straight to the database, as an installed server would have it.
  var path=Path.Combine(Path.GetTempPath(),"tm-alarm-email-"+Guid.NewGuid());Directory.CreateDirectory(path);
  var c=new Configuration();c.Settings.Simulation=false;
  c.Settings.MailEnabled=true;c.Settings.AutomaticAlerts=true;c.Settings.SmtpHost="127.0.0.1";c.Settings.SmtpPort=smtpPort;c.Settings.MailSecurity="None";c.Settings.SenderEmail="watchdog@example.test";c.Settings.SenderName="Watchdog";c.Settings.TestRecipient="test@example.test";
  var controller=new Controller{Name="F2A",Protocol=Protocol.TCP,Host="127.0.0.1",TcpPort=plcPort,PollSeconds=1,TimeoutMs=1000,Retries=0};c.Controllers.Add(controller);
  var sensor=new Sensor{ControllerId=controller.Id,Name="CL-01",TemperatureOffset=4196,Multiplier=0.1,Decimals=1,SoftwareHighLimit=21,SoftwareLowLimit=18,AlarmDelaySeconds=2,AlarmHysteresis=0.5,EmailEnabled=true,Recipients="alarm@example.test",EmailHigh=true,EmailLow=true,RecoveryEmail=true};
  c.Sensors.Add(sensor);
  new Store(Path.Combine(path,"watchdog.db")).Save(c,"Test","Live alarm email flow fixture");
  var state=new ServerState(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string>{{"Storage:DataDirectory",path}}).Build());
  var worker=new NotificationWorker(state);
  await state.StartAsync(default);await worker.StartAsync(default);
  try
  {
   async Task<bool> Wait(Func<bool> condition,int seconds){for(int i=0;i<seconds*10;i++){if(condition())return true;await Task.Delay(100);}return condition();}
   string Level()=>state.Notifications.Alarms.Current(state.Engine.Config.Sensors[0],state.Engine.Display(state.Engine.Config.Sensors[0])).Level;
   Check(await Wait(()=>state.Engine.Display(state.Engine.Config.Sensors[0]).Quality=="VALID",10),"live reading from the PLC is VALID (20.0 °C)");
   Check(Level()=="Normal"&&mails.Count==0,"no alarm and no email inside the limits");
   raw=255; // 25.5 °C, above High 21
   Check(await Wait(()=>Level()=="High",15),"high alarm activates after the 2 s delay");
   Check(await Wait(()=>{lock(mails)return mails.Any(m=>m.Data.Contains("High temperature"));},20),"high-alarm email reaches the SMTP server");
   (string Rcpt,string Data) high;lock(mails)high=mails.First(m=>m.Data.Contains("High temperature"));
   Check(high.Rcpt.Contains("alarm@example.test")&&high.Data.Contains("CL-01")&&high.Data.Contains("25.5"),"email goes to the sensor recipients and names sensor and temperature");
   raw=195; // 19.5 °C, back inside
   Check(await Wait(()=>{lock(mails)return mails.Any(m=>m.Data.Contains("Return to normal"));},20),"return-to-normal email sent");

   // V4.4.1 readiness: a fully configured live sensor is ready.
   var live=state.Engine.Config;var cl=live.Sensors[0];
   Check(AlarmNotifications.GlobalIssues(live).Length==0&&AlarmNotifications.SensorIssues(live,cl).Length==0,"configured sensor reported ready");
   Check(AlarmNotifications.Coverage(cl).Contains("above 21")&&AlarmNotifications.Coverage(cl).Contains("below 18")&&AlarmNotifications.Coverage(cl).Contains("alarm@example.test"),"readiness explains which temperatures email whom");

   // V4.4.1 per-sensor test alarm email reaches the sensor's recipients.
   int before;lock(mails)before=mails.Count;
   var test=await SmtpEndpoints.SensorTest(state,cl.Id,default);
   Check(((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)test).StatusCode is null or 200,"test alarm email accepted");
   Check(await Wait(()=>{lock(mails)return mails.Count>before&&mails[^1].Data.Contains("TEST alarm email")&&mails[^1].Rcpt.Contains("alarm@example.test");},10),"test alarm email reaches the sensor recipients");
   Check(state.Store.Audits().Any(x=>x.Contains("Test alarm email submitted")&&!x.Contains("alarm@example.test")),"test alarm email audited without addresses");
  }
  finally{await worker.StopAsync(default);await state.StopAsync(default);stop.Cancel();plc.Stop();smtp.Stop();}
 }
}
