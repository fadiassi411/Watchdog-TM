using System.Text.Json;
using Watchdog.TM;

public sealed class ServerState : IHostedService
{
    public string DirectoryPath { get; }
    public Store Store { get; }
    public MonitorEngine Engine { get; }
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
    }
    public Configuration Clone() => JsonSerializer.Deserialize<Configuration>(JsonSerializer.Serialize(Engine.Config))!;
    public async Task Save(Configuration c,string action)
    {
        Rules.Validate(c); Security.Capacity(c,Engine.Config);
        foreach(var controller in c.Controllers) if(!Enum.IsDefined(controller.Protocol)||!Enum.IsDefined(controller.Parity)||!Enum.IsDefined(controller.StopBits)) throw new Exception("Choose valid controller connection options.");
        foreach(var sensor in c.Sensors) if(!Enum.IsDefined(sensor.TemperatureFunction)||!Enum.IsDefined(sensor.Format)||!Enum.IsDefined(sensor.Order)) throw new Exception("Choose valid temperature format options.");
        LiveMonitoring.Apply(c);
        Store.Save(c,"Administrator",action);
        await Engine.Replace(c);
        Revision=Guid.NewGuid().ToString();
    }
    public Task StartAsync(CancellationToken ct) { Engine.Start(); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) => Engine.Stop();
}

