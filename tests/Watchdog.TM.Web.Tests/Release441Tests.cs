using Watchdog.TM;

// V4.4.1: the Email page explains exactly why a sensor would not send an alarm email.
public static class Release441Tests
{
 public static Task Run()
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS 4.4.1 "+name);}
  Configuration Live()
  {
   var c=new Configuration();c.Settings.Simulation=false;c.Settings.MailEnabled=true;c.Settings.AutomaticAlerts=true;c.Settings.SmtpHost="smtp.example.test";c.Settings.SmtpPort=587;c.Settings.MailSecurity="StartTls";c.Settings.SenderEmail="watchdog@example.test";
   var p=new Controller{Protocol=Protocol.TCP,Host="10.0.0.1"};c.Controllers.Add(p);
   var s=new Sensor{ControllerId=p.Id,Name="CL-01",TemperatureOffset=4196,SoftwareHighLimit=21,SoftwareLowLimit=18,EmailEnabled=true,Recipients="a@example.test"};c.Sensors.Add(s);c.Settings.CommissionedSensors.Add(s.Id);
   return c;
  }
  bool Has(string[] issues,string text)=>issues.Any(x=>x.Contains(text,StringComparison.OrdinalIgnoreCase));

  var ok=Live();Check(AlarmNotifications.GlobalIssues(ok).Length==0&&AlarmNotifications.SensorIssues(ok,ok.Sensors[0]).Length==0,"complete configuration is ready");
  var c=Live();c.Settings.AutomaticAlerts=false;Check(Has(AlarmNotifications.GlobalIssues(c),"Automatically email temperature alarms"),"automatic alerts off is reported (test email still works)");
  c=Live();c.Settings.MailEnabled=false;Check(Has(AlarmNotifications.GlobalIssues(c),"Enable temperature alarm email"),"global email switch off is reported");
  c=Live();c.Settings.Simulation=true;Check(Has(AlarmNotifications.GlobalIssues(c),"Demonstration mode"),"demonstration mode is reported");
  c=Live();c.Settings.SenderEmail="";Check(Has(AlarmNotifications.GlobalIssues(c),"SMTP settings are incomplete"),"incomplete SMTP settings are reported");
  c=Live();c.Sensors[0].EmailEnabled=false;Check(Has(AlarmNotifications.SensorIssues(c,c.Sensors[0]),"Enable email for this sensor"),"sensor email off is reported");
  c=Live();c.Sensors[0].Recipients="";Check(Has(AlarmNotifications.SensorIssues(c,c.Sensors[0]),"No recipients"),"missing recipients are reported");
  c=Live();c.Sensors[0].SoftwareHighLimit=null;Check(Has(AlarmNotifications.SensorIssues(c,c.Sensors[0]),"no High Alarm limit"),"missing High limit is reported");
  c=Live();c.Sensors[0].EmailHigh=false;c.Sensors[0].EmailLow=false;Check(Has(AlarmNotifications.SensorIssues(c,c.Sensors[0]),"Neither High nor Low"),"no alarm type selected is reported");
  c=Live();c.Controllers[0].Enabled=false;Check(Has(AlarmNotifications.SensorIssues(c,c.Sensors[0]),"controller is disabled"),"disabled controller is reported");
  c=Live();c.Settings.CommissionedSensors.Clear();Check(Has(AlarmNotifications.SensorIssues(c,c.Sensors[0]),"not in live monitoring"),"sensor outside live monitoring is reported");
  // Readiness must agree with the rule that actually queues email.
  foreach(var cfg in new[]{Live()}){Check(AlarmNotifications.Eligible(cfg,cfg.Sensors[0]),"ready sensor is eligible for automatic email");}
  c=Live();c.Settings.AutomaticAlerts=false;Check(!AlarmNotifications.Eligible(c,c.Sensors[0]),"blocked sensor is not eligible");
  return Task.CompletedTask;
 }
}
