using System.Text.Json;
using Microsoft.Data.Sqlite;
using Watchdog.TM;

public sealed class ServerState : IHostedService
{
    public string DirectoryPath { get; }
    public Store Store { get; }
    public MonitorEngine Engine { get; }
    public AlarmNotifications Notifications { get; }
    public PowerAvailability PowerAvailability { get; } = new();
    public string MailFault { get; set; } = "";
    public SemaphoreSlim Gate { get; } = new(1,1);
    public const long MaximumRestoreBytes = 8L*1024*1024*1024;
    // Automatic safety copies made before restore, deletion and timestamp corrections.
    public const int SafetyBackupsKept = 10;
    static readonly string[] SafetyBackupPrefixes = ["before-restore-","before-delete-","before-plc-history-offset-"];
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
    public async Task Save(Configuration c,string action,Action<SqliteConnection>? databaseMutation=null)
    {
        foreach(var controller in c.Controllers) PlcHistory.Validate(controller,c);
        Rules.Validate(c); Security.Capacity(c,Engine.Config);
        foreach(var controller in c.Controllers) if(!Enum.IsDefined(controller.Protocol)||!Enum.IsDefined(controller.Parity)||!Enum.IsDefined(controller.StopBits)) throw new Exception("Choose valid controller connection options.");
        foreach(var sensor in c.Sensors) if(!Enum.IsDefined(sensor.TemperatureFunction)||!Enum.IsDefined(sensor.Format)||!Enum.IsDefined(sensor.Order)) throw new Exception("Choose valid temperature format options.");
        LiveMonitoring.Apply(c);
        Notifications.Ensure();
        Store.Write(db=>
        {
            Store.EnsureHistory(db);
            databaseMutation?.Invoke(db);
            Store.Run(db,"INSERT INTO config VALUES(1,$0) ON CONFLICT(id) DO UPDATE SET json=$0",JsonSerializer.Serialize(c));
            Store.Audit(db,"Administrator",action);
        });
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
        PowerAvailability.Apply(c.Settings.KeepComputerAwake && !c.Settings.Simulation && c.Controllers.Any(x=>x.Enabled) && c.Sensors.Any(x=>x.Enabled&&!x.Retired));
        if(c.Settings.Simulation||!c.Settings.MailEnabled||!c.Settings.AutomaticAlerts) Notifications.Reset();
        Revision=Guid.NewGuid().ToString();
    }
    // Keeps the newest SafetyBackupsKept files of each kind so the data folder cannot fill the disk.
    // Daily backups in the configured backup folder and the installer migration backup are not touched.
    public void PruneSafetyBackups()
    {
        foreach(var prefix in SafetyBackupPrefixes)
        {
            try
            {
                var old=new DirectoryInfo(DirectoryPath).EnumerateFiles(prefix+"*.db").OrderByDescending(x=>x.LastWriteTimeUtc).Skip(SafetyBackupsKept).ToArray();
                foreach(var file in old) try{file.Delete();}catch(IOException){}catch(UnauthorizedAccessException){}
            }
            catch(IOException){}
        }
    }
    public Task StartAsync(CancellationToken ct) { Engine.Start(); PowerAvailability.Apply(Engine.Config.Settings.KeepComputerAwake && !Engine.Config.Settings.Simulation && Engine.Config.Controllers.Any(x=>x.Enabled) && Engine.Config.Sensors.Any(x=>x.Enabled&&!x.Retired)); return Task.CompletedTask; }
    public async Task StopAsync(CancellationToken ct) { PowerAvailability.Apply(false); await Engine.Stop(); }
}

public static class TemporaryFiles
{
    // The file is removed by Windows when the response stream closes, even if the download is cancelled.
    public static FileStream OpenAndDelete(string path)=>new(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,81920,FileOptions.DeleteOnClose|FileOptions.SequentialScan);
    public static void Delete(string path){try{if(File.Exists(path))File.Delete(path);}catch(IOException){}}
}

