using System.Collections.Concurrent;
using System.IO.Ports;
using System.Net.Sockets;
using NModbus;
using NModbus.IO;
namespace Watchdog.TM;

public sealed class PortAdapter(SerialPort port) : IStreamResource
{
    public int InfiniteTimeout => SerialPort.InfiniteTimeout; public int ReadTimeout
    {
        get => port.ReadTimeout; set => port.ReadTimeout = value;
    }
    public int WriteTimeout
    {
        get => port.WriteTimeout; set => port.WriteTimeout = value;
    }
    public void DiscardInBuffer() => port.DiscardInBuffer(); public int Read(byte[] buffer, int offset, int count) => port.Read(buffer, offset, count); public void Write(byte[] buffer, int offset, int count) => port.Write(buffer, offset, count); public void Dispose() => port.Dispose();
}
public sealed class Transport
{
    readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();
    public async Task<T> Serialize<T>(string key, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = locks.GetOrAdd(key, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally { gate.Release(); }
    }
    public ConcurrentDictionary<Guid, string> Scenarios { get; } = new();
    readonly ConcurrentDictionary<Guid, double> limits = new();
    public async Task<T> Session<T>(Controller c, Func<IModbusMaster, T> action, CancellationToken ct)
    {
        var key = c.Protocol == Protocol.RTU ? c.ComPort.Trim().ToUpperInvariant() : c.Id.ToString();
        return await Serialize(key, () => Task.Run(() => { ct.ThrowIfCancellationRequested(); using var tcp = c.Protocol == Protocol.TCP ? new TcpClient() : null; using var port = c.Protocol == Protocol.RTU ? new SerialPort(c.ComPort, c.BaudRate, c.Parity, c.DataBits, c.StopBits) : null; IModbusMaster master; if (tcp != null) { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(c.TimeoutMs); tcp.ConnectAsync(c.Host, c.TcpPort, timeout.Token).AsTask().GetAwaiter().GetResult(); tcp.ReceiveTimeout = c.TimeoutMs; tcp.SendTimeout = c.TimeoutMs; master = new ModbusFactory().CreateMaster(tcp); } else { port!.ReadTimeout = c.TimeoutMs; port.WriteTimeout = c.TimeoutMs; port.Open(); master = new ModbusFactory().CreateRtuMaster(new PortAdapter(port)); } using (master) { master.Transport.ReadTimeout = c.TimeoutMs; master.Transport.WriteTimeout = c.TimeoutMs; master.Transport.Retries = 0; return action(master); } }, ct), ct);
    }
    static ushort[] Reg(IModbusMaster m, Controller c, ushort address, RegisterFunction fn, ushort count) => fn == RegisterFunction.Holding03 ? m.ReadHoldingRegisters(c.UnitId, address, count) : m.ReadInputRegisters(c.UnitId, address, count);
    public Task<double> ReadTemperature(Controller c,Sensor s,CancellationToken ct)
    {
        if(c.Protocol==Protocol.Simulation||!s.TemperatureOffset.HasValue)throw new IOException("Choose a real controller and enter a verified temperature register.");
        return Session(c,m=>Rules.Decode(Reg(m,c,s.TemperatureOffset.Value,s.TemperatureFunction,(ushort)(s.Format is ValueFormat.Int16 or ValueFormat.UInt16?1:2)),s.Format,s.Order,s.Multiplier,s.Offset),ct);
    }
    public Task<Reading> Read(Controller c, Sensor s, bool simulation, CancellationToken ct)
    {
        if (simulation)
        {
            ct.ThrowIfCancellationRequested();
            var scenario = Scenarios.GetValueOrDefault(s.Id, "Normal");
            if (Scenarios.GetValueOrDefault(c.Id) == "Offline")
                throw new IOException("Simulated controller disconnected.");
            double raw = scenario == "Negative" ? -125 : 45 + Math.Round(Math.Sin(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 12.0 + s.TemperatureOffset.GetValueOrDefault()) * 6);
            return Task.FromResult(new Reading(DateTimeOffset.UtcNow, scenario == "Fault" ? null : raw * .1, limits.GetOrAdd(s.Id, 8), scenario == "High", scenario == "Low", scenario == "Fault", scenario == "Fault" ? "SENSOR FAULT" : "VALID"));
        }
        if (c.Protocol == Protocol.Simulation || !s.Mapped)
            throw new IOException("Live mapping incomplete; commissioning required.");
        return ReadOnly(); async Task<Reading> ReadOnly() { var value = await ReadTemperature(c, s, ct); return new Reading(DateTimeOffset.UtcNow, value, null, null, null, false, "VALID"); }
    }
}
public sealed class MonitorEngine
{
    public Configuration Config
    {
        get; private set;
    }
    public Store Store
    {
        get;
    }
    public Transport Transport { get; } = new();
    public ConcurrentDictionary<Guid, Reading> Readings { get; } = new(); public ConcurrentDictionary<Guid, ControllerState> States { get; } = new();
    public bool Silenced
    {
        get; set;
    }
    public string OperationalFault { get; private set; } = "";
    CancellationTokenSource? stop; Task[] tasks = []; readonly ConcurrentDictionary<Guid, DateTimeOffset> nextSample = new();
    public MonitorEngine(Configuration c, Store store)
    {
        Config = c;
        Store = store;
    }
    public event Action<Sensor, Reading>? ReadingObserved;
    public void Start()
    {
        foreach (var item in Store.Latest(Config.Settings.Simulation))
            Readings[item.Key] = item.Value;
        stop = new();
        var ct = stop.Token;
        tasks = Config.Controllers.Select(c => Task.Run(() => Poll(c, ct))).Append(Task.Run(() => SampleLoop(ct))).Append(Task.Run(() => BackupLoop(ct))).ToArray();
    }
    public async Task Stop()
    {
        if (stop == null)
            return;
        stop.Cancel();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        stop.Dispose();
        stop = null;
    }
    public async Task Replace(Configuration c)
    {
        await Stop();
        Config = c;
        Readings.Clear();
        States.Clear();
        nextSample.Clear();
        Start();
    }
    bool Allowed(Sensor s) => Config.Settings.Simulation || Config.Settings.CommissionedSensors.Contains(s.Id);
    async Task Poll(Controller c, CancellationToken ct)
    {
        int failures = 0;
        var state = States.GetOrAdd(c.Id, _ => new());
        while (!ct.IsCancellationRequested)
        {
            if (!c.Enabled)
            {
                state.Status = "Disabled";
                await Task.Delay(1000, ct);
                continue;
            }
            bool failed = false;
            foreach (var s in Config.Sensors.Where(x => x.ControllerId == c.Id && x.Enabled && !x.Retired))
            {
                if (!Allowed(s))
                {
                    state.Status = "Offline";
                    state.Error = "Valid license and commissioning required.";
                    continue;
                }
                try
                {
                    Reading? read = null;
                    for (int attempt = 0; attempt <= c.Retries; attempt++)
                    {
                        try
                        {
                            read = await Transport.Read(c, s, Config.Settings.Simulation, ct);
                            break;
                        }
                        catch when (attempt < c.Retries && !ct.IsCancellationRequested) { await Task.Delay(200, ct); }
                    }
                    Readings[s.Id] = read!;
                    state.LastSuccess = read!.At;
                    state.Status = "Connected";
                    state.Error = "";
                    try
                    {
                        Store.Latest(s.Id, read, Config.Settings.Simulation);
                        ReadingObserved?.Invoke(s, read);
                        if (read.High.HasValue && Store.Observe(s, "HIGH", read.High.Value, read.At, Config.Settings.Simulation))
                            Silenced = false;
                        if (read.Low.HasValue && Store.Observe(s, "LOW", read.Low.Value, read.At, Config.Settings.Simulation))
                            Silenced = false;
                    }
                    catch { }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex) { failed = true; state.Error = ex is IOException ? ex.Message : "Communication failed. Check connection and PLC map."; state.Status = failures >= 2 ? "Offline" : "Reconnecting"; break; }
            }
            failures = failed ? Math.Min(failures + 1, 6) : 0;
            await Task.Delay(TimeSpan.FromSeconds(failed ? Math.Min(30, Math.Pow(2, failures)) : c.PollSeconds), ct);
        }
    }
    public Reading Display(Sensor s)
    {
        if (s.Retired || !s.Enabled)
            return new(DateTimeOffset.MinValue, null, null, null, null, false, s.Retired ? "RETIRED" : "DISABLED");
        var c = Config.Controllers.First(x => x.Id == s.ControllerId);
        Readings.TryGetValue(s.Id, out var r);
        if (!c.Enabled)
            return (r ?? new(DateTimeOffset.MinValue, null, null, null, null, false, "DISABLED")) with
            {
                Quality = "DISABLED"
            };
        if (r == null)
            return new(DateTimeOffset.MinValue, null, null, null, null, false, "OFFLINE");
        if (States.GetValueOrDefault(c.Id)?.Status != "Connected")
            return r with
            {
                Quality = "OFFLINE"
            };
        if (r.At > DateTimeOffset.UtcNow.AddSeconds(1) || DateTimeOffset.UtcNow - r.At > TimeSpan.FromSeconds(Math.Max(c.PollSeconds * 3, c.TimeoutMs / 1000.0 * 2 + 2)))
            return r with
            {
                Quality = "STALE"
            };
        return r;
    }
    async Task SampleLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var s in Config.Sensors.Where(x => !x.Retired))
            {
                if (!Config.Settings.Simulation && Config.Controllers.Any(c=>c.Id==s.ControllerId && c.History.Enabled && c.History.Channels.Any(ch=>ch.SensorId==s.Id))) continue;
                var now = DateTimeOffset.UtcNow;
                DateTimeOffset due; try { due = nextSample.GetOrAdd(s.Id, _ => { var last = Store.LastSample(s.Id); return last?.AddSeconds(s.SampleSeconds) ?? now; }); } catch { OperationalFault = "RECORDING FAULT — cannot read sampling schedule. Retrying."; continue; }
                if (now < due)
                    continue;
                var r = Display(s);
                try
                { // One observed sample now; never fabricate measurements for a stopped application.
                    Store.Sample(new(s.Id, now, r.Quality == "VALID" ? r.Temperature : null, Config.Settings.Simulation ? "SIMULATION / " + r.Quality : r.Quality));
                    nextSample[s.Id] = now.AddSeconds(s.SampleSeconds);
                }
                catch { }
            }
            await Task.Delay(500, ct);
        }
    }
    async Task BackupLoop(CancellationToken ct)
    {
        DateOnly? last = null;
        while (!ct.IsCancellationRequested)
        {
            var settings = Config.Settings;
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (settings.DailyBackup && last != today)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(settings.BackupFolder))
                        throw new IOException();
                    Directory.CreateDirectory(settings.BackupFolder);
                    var path = System.IO.Path.Combine(settings.BackupFolder, $"WatchdogTM-{DateTime.Now:yyyyMMdd}.db");
                    Store.Backup(path);
                    foreach (var file in Directory.EnumerateFiles(settings.BackupFolder, "WatchdogTM-????????.db"))
                    {
                        if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-settings.RetentionDays))
                            File.Delete(file);
                    }
                    last = today;
                    OperationalFault = "";
                }
                catch { OperationalFault = "BACKUP FAILED — check backup folder and available space."; }
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}





