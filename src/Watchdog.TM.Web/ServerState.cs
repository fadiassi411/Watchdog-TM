using System.Text.Json;
using Watchdog.TM;

public sealed class ServerState : IHostedService
{
    public string DirectoryPath { get; }
    public Store Store { get; }
    public MonitorEngine Engine { get; }
    public AlarmNotifications Notifications { get; }
    public string MailFault { get; set; } = "";
    public SemaphoreSlim Gate { get; } = new(1,1);
    public string Revision { get; private set; } = Guid.NewGuid().ToString();
    public ServerState(IConfiguration config)
    {
        DirectoryPath = config["Storage:DataDirectory"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Watchdog TM");
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath,"watchdog.db");
        bool fresh = !File.Exists(path);
        Store = new Store(path);
        if(fresh) Store.Save(new Configuration(),"System","New temperature-only web server");
        var c=Store.Load(); Rules.Validate(c);
        var previousIds=c.Settings.CommissionedSensors.ToArray();
        LiveMonitoring.Apply(c);
        if(!previousIds.SequenceEqual(c.Settings.CommissionedSensors))
            Store.Save(c,"System","Automatically resumed configured live readings");
        Engine=new MonitorEngine(c,Store);
        Notifications=new AlarmNotifications(Store);
        Engine.ReadingObserved+=(sensor,reading)=>{try{Notifications.Observe(Engine.Config,sensor,reading);}catch{MailFault="Temperature alarm observation could not be saved. Check server storage.";}};
    }
    public Configuration Clone() => JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(Engine.Config))!;
    public async Task Save(Configuration c,string action)
    {
        foreach(var controller in c.Controllers) PlcHistory.Validate(controller,c);
        Rules.Validate(c); Security.Capacity(c,Engine.Config);
        foreach(var controller in c.Controllers) if(!Enum.IsDefined(controller.Protocol)||!Enum.IsDefined(controller.Parity)||!Enum.IsDefined(controller.StopBits)) throw new Exception("Choose valid controller connection options.");
        foreach(var sensor in c.Sensors) if(!Enum.IsDefined(sensor.TemperatureFunction)||!Enum.IsDefined(sensor.Format)||!Enum.IsDefined(sensor.Order)) throw new Exception("Choose valid temperature format options.");
        LiveMonitoring.Apply(c);
        Notifications.Ensure();
        Store.Write(Store.EnsureHistory);

        Store.Save(c,"Administrator",action);
        foreach(var old in Engine.Config.Sensors)
        {
            var next=c.Sensors.FirstOrDefault(x=>x.Id==old.Id);
            if(next is null || next.SoftwareHighLimit!=old.SoftwareHighLimit || next.SoftwareLowLimit!=old.SoftwareLowLimit ||
               next.AlarmDelaySeconds!=old.AlarmDelaySeconds || next.AlarmHysteresis!=old.AlarmHysteresis || next.Enabled!=old.Enabled || next.Retired!=old.Retired ||
               next.ControllerId!=old.ControllerId || next.TemperatureOffset!=old.TemperatureOffset || next.Multiplier!=old.Multiplier || next.Offset!=old.Offset ||
               next.TemperatureFunction!=old.TemperatureFunction || next.Format!=old.Format || next.Order!=old.Order ||
               c.Controllers.First(x=>x.Id==next.ControllerId).Enabled!=Engine.Config.Controllers.First(x=>x.Id==old.ControllerId).Enabled ||
               c.Settings.Simulation!=Engine.Config.Settings.Simulation)
            {Notifications.Alarms.Reset(old.Id);Notifications.Reset(old.Id);}
        }
        await Engine.Replace(c);
        if(c.Settings.Simulation||!c.Settings.MailEnabled||!c.Settings.AutomaticAlerts) Notifications.Reset();
        Revision=Guid.NewGuid().ToString();
    }
    public Task StartAsync(CancellationToken ct) { Engine.Start(); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) => Engine.Stop();
}

