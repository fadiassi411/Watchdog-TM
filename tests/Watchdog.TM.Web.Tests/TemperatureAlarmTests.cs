#nullable enable
using Watchdog.TM;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

public static class TemperatureAlarmTests
{
    public static async Task Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"tm-alarms-"+Guid.NewGuid());Directory.CreateDirectory(root);
        int checks=0;
        ServerState? server=null;
        void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS temperature alarms "+name);}
        try
        {
            var store=new Store(Path.Combine(root,"test.db"));var notifications=new AlarmNotifications(store);var alarms=notifications.Alarms;
            var sensor=new Sensor{SoftwareHighLimit=8,SoftwareLowLimit=2,AlarmDelaySeconds=10,AlarmHysteresis=1};
            var start=DateTimeOffset.UtcNow.AddMinutes(-20);
            Reading R(double? value,int seconds,string quality="VALID")=>new(start.AddSeconds(seconds),value,null,null,null,false,quality);
            TemperatureAlarmStatus O(double value,int seconds)=>alarms.Observe(sensor,R(value,seconds),15);
            Check(O(9,0).Pending=="High","high condition starts delay");
            Check(O(9,9).Level=="Normal","short excursion does not activate");
            Check(O(8,10).Pending=="","exact high limit cancels pending activation");
            O(9,11);var high=O(9,21);Check(high.Level=="High"&&high.Since==start.AddSeconds(11),"continuous samples activate at delay boundary");
            Check(O(7.5,22).Episode==high.Episode,"hysteresis retains high episode below limit");
            Check(!alarms.Acknowledge(sensor.Id,"stale"),"stale acknowledgement rejected");
            Check(alarms.Acknowledge(sensor.Id,high.Episode),"active episode acknowledged");
            var ack=alarms.Current(sensor,R(7.5,22));Check(ack.Level=="High"&&ack.AcknowledgedAt is not null,"acknowledgement does not clear condition");
            var restarted=new TemperatureAlarms(store);Check(restarted.Current(sensor,R(7.5,22)).AcknowledgedAt==ack.AcknowledgedAt,"acknowledgement survives restart");
            Check(O(7,23).Level=="Normal","exact high reset boundary clears");
            O(9,24);var next=O(9,34);Check(next.Episode!=high.Episode&&next.AcknowledgedAt is null,"new episode requires new acknowledgement");
            Check(!alarms.Acknowledge(sensor.Id,high.Episode),"old browser cannot acknowledge new episode");
            O(7,35);O(1,36);Check(O(1,46).Level=="Low","low delay activates");
            Check(O(2.5,47).Level=="Low","low hysteresis retains alarm");Check(O(3,48).Level=="Normal","exact low reset boundary clears");
            O(9,49);alarms.Observe(sensor,R(null,50,"OFFLINE"),15);Check(O(9,59).Level=="Normal","unavailable reading interrupts delay");
            Check(O(9,69).Level=="High","delay resumes from first valid sample");
            Check(alarms.Current(sensor,R(9,70,"STALE")).Level=="Unavailable","stale dashboard does not claim current alarm");
            O(7,70);O(9,71);Check(O(9,100).Level=="Normal","long sample gap restarts delay");
            Check(O(9,99).Level=="Normal","out of order sample cannot advance delay");
            var pendingRestart=new TemperatureAlarms(store);Check(pendingRestart.Observe(sensor,R(9,110),15).Level=="Normal","restart interrupts pending delay");
            O(9,120);Check(O(1,121).Level=="Normal","opposite excursion ends old alarm before new delay");
            Check(O(1,131).Level=="Low","opposite alarm satisfies independent delay");
            sensor.AlarmDelaySeconds=0;sensor.AlarmHysteresis=0;alarms.Reset(sensor.Id);
            Check(O(8,120).Level=="Normal"&&O(9,121).Level=="High"&&O(8,122).Level=="Normal","zero settings preserve original boundaries");
            var controller=new Controller{Protocol=Protocol.TCP,PollSeconds=60};sensor.ControllerId=controller.Id;sensor.EmailEnabled=true;sensor.Recipients="test@example.test";sensor.Reminders=true;sensor.ReminderMinutes=1;sensor.RecoveryEmail=true;
            var config=new Configuration();config.Settings.Simulation=false;config.Settings.MailEnabled=true;config.Settings.AutomaticAlerts=true;config.Controllers.Add(controller);config.Sensors.Add(sensor);config.Settings.CommissionedSensors.Add(sensor.Id);
            alarms.Reset(sensor.Id);notifications.Reset(sensor.Id);
            void Observe(double value,int seconds)=>notifications.Observe(config,sensor,R(value,seconds));
            JsonElement[] Rows()=>notifications.History().Select(x=>JsonSerializer.SerializeToElement(x)).ToArray();
            Observe(9,130);Observe(9,190);Check(Rows().Count(x=>x.GetProperty("kind").GetString()=="High reminder")==1,"active episode queues reminder");
            var episode=alarms.Current(sensor,R(9,190)).Episode;alarms.Acknowledge(sensor.Id,episode);
            Check(Rows().Single(x=>x.GetProperty("kind").GetString()=="High reminder").GetProperty("status").GetString()=="Cancelled","acknowledgement cancels queued reminder");
            Observe(9,250);Check(Rows().Count(x=>x.GetProperty("kind").GetString()=="High reminder")==1,"acknowledged episode suppresses future reminders");
            Observe(8,251);Check(Rows().Any(x=>x.GetProperty("kind").GetString()=="Return to normal"),"acknowledgement preserves recovery notification");
            Observe(9,252);Observe(9,312);Check(Rows().Count(x=>x.GetProperty("kind").GetString()=="High reminder")==2,"new episode restores reminders");
            sensor.AlarmDelaySeconds=10;sensor.AlarmHysteresis=1;alarms.Reset(sensor.Id);notifications.Reset(sensor.Id);
            var before=Rows().Length;Observe(9,320);Check(Rows().Length==before,"pending alarm sends no initial email");Observe(9,330);Observe(7.5,331);Observe(8.5,332);
            Check(Rows().Length==before+1,"mail shares delay and hysteresis without duplicate initial notifications");
            Check(JsonSerializer.Deserialize<Sensor>("{}")!.AlarmDelaySeconds==0,"old configuration defaults to immediate alarms");
            foreach(var invalid in new[]{-1,86401}){sensor.AlarmDelaySeconds=invalid;try{Rules.Validate(config);throw new Exception("Invalid delay accepted");}catch(Exception e)when(e.Message.StartsWith("Alarm delay")){checks++;}}
            sensor.AlarmDelaySeconds=0;
            foreach(var invalid in new[]{-1d,double.NaN,double.PositiveInfinity,6d}){sensor.AlarmHysteresis=invalid;try{Rules.Validate(config);throw new Exception("Invalid hysteresis accepted");}catch(Exception e)when(e.Message.StartsWith("Alarm delay")){checks++;}}
            server=new ServerState(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Storage:DataDirectory",Path.Combine(root,"server")}}).Build());
            var setup=server.Clone();var plc=new Controller{Protocol=Protocol.Simulation,Enabled=false};var item=new Sensor{ControllerId=plc.Id,TemperatureOffset=1,SoftwareHighLimit=-100};setup.Controllers.Add(plc);setup.Sensors.Add(item);await server.Save(setup,"alarm test setup");await server.StopAsync(default);
            var active=server.Notifications.Alarms.Observe(item,R(9,400),15);server.Notifications.Alarms.Acknowledge(item.Id,active.Episode);
            setup=server.Clone();setup.Sensors[0].Name="Renamed";await server.Save(setup,"rename");
            Check(server.Notifications.Alarms.IsAcknowledged(item.Id),"unrelated configuration edit preserves acknowledgement");
            setup=server.Clone();setup.Sensors[0].AlarmHysteresis=1;await server.Save(setup,"alarm settings changed");
            await server.StopAsync(default);
            Check(server.Notifications.Alarms.Current(item,R(9,400)).Level=="Normal","alarm configuration edit resets episode");
            // Simulate restoration of an older backup without the newly added table.
            server.Store.Write(db=>Store.Run(db,"DROP TABLE temperature_alarm_state"));server.Notifications.Ensure();server.Notifications.Alarms.ResetAll();
            Check(server.Notifications.Alarms.Observe(item,R(9,401),15).Level=="High","old backup schema gains alarm state table");
            Console.WriteLine($"Temperature alarm checks passed: {checks}");
        }
        finally{if(server is not null)await server.StopAsync(default);Directory.Delete(root,true);}
        await Task.CompletedTask;
    }
}
