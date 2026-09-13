using System.ComponentModel;
using System.Text.Json.Serialization;
namespace Watchdog.TM;

public enum Protocol
{
    Simulation, TCP, RTU
}
public enum RegisterFunction
{
    Holding03 = 3, Input04 = 4
}
public enum BitFunction
{
    Coil01 = 1, Discrete02 = 2, Holding03 = 3, Input04 = 4
}
public enum ValueFormat
{
    Int16, UInt16, Int32, UInt32, Float32
}
public enum ByteOrder
{
    ABCD, BADC, CDAB, DCBA
}
public sealed class Controller
{
    public PlcHistoryLayout History { get; set; } = new();
    [Browsable(false)] public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New controller";
    public bool Enabled { get; set; } = true;
    public Protocol Protocol { get; set; } = Protocol.TCP;
    public string Host { get; set; } = ""; public int TcpPort { get; set; } = 502;
    public byte UnitId { get; set; } = 1; public string ComPort { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600; public System.IO.Ports.Parity Parity { get; set; } = System.IO.Ports.Parity.Even;
    public int DataBits { get; set; } = 8; public System.IO.Ports.StopBits StopBits { get; set; } = System.IO.Ports.StopBits.One;
    public int PollSeconds { get; set; } = 2; public int TimeoutMs { get; set; } = 1500; public int Retries { get; set; } = 1;
    [JsonIgnore, Browsable(false)] public string SerialKey => $"{BaudRate}/{Parity}/{DataBits}/{StopBits}";
    public override string ToString() => Name;
}
public sealed class Sensor
{
    [Browsable(false)] public Guid Id { get; set; } = Guid.NewGuid();
    [Browsable(false)]
    public Guid ControllerId
    {
        get; set;
    }
    public string Name { get; set; } = "New refrigerator"; public string Location { get; set; } = "";
    public bool Enabled { get; set; } = true; public bool Retired { get; set; } = false;
    public ushort? TemperatureOffset
    {
        get; set;
    }
    public RegisterFunction TemperatureFunction { get; set; } = RegisterFunction.Holding03;
    public ValueFormat Format { get; set; } = ValueFormat.Int16; public ByteOrder Order { get; set; } = ByteOrder.ABCD;
    public double Multiplier { get; set; } = 1; public double Offset { get; set; } = 0; public int Decimals { get; set; } = 1;
    // Local Watchdog thresholds, never Modbus addresses or writable PLC values.
    public double? SoftwareHighLimit { get; set; }
    public double? SoftwareLowLimit { get; set; }
    public ushort? HighAlarmOffset
    {
        get; set;
    }
    public BitFunction HighAlarmFunction { get; set; } = BitFunction.Coil01; public int HighAlarmRegisterBit { get; set; } = 0;
    public ushort? HighLimitOffset
    {
        get; set;
    }
    public double LimitMultiplier { get; set; } = 1;
    public double MinimumLimit { get; set; } = -40; public double MaximumLimit { get; set; } = 40;
    public ushort? FaultOffset
    {
        get; set;
    }
    public BitFunction FaultFunction { get; set; } = BitFunction.Coil01; public int FaultRegisterBit { get; set; } = 0;
    public bool LowAlarmEnabled
    {
        get; set;
    }
    public ushort? LowAlarmOffset
    {
        get; set;
    }
    public BitFunction LowAlarmFunction { get; set; } = BitFunction.Coil01; public int LowAlarmRegisterBit
    {
        get; set;
    }
    public ushort? LowLimitOffset
    {
        get; set;
    }
    public int SampleSeconds { get; set; } = 600;
    public bool EmailEnabled
    {
        get; set;
    }
    public string Recipients { get; set; } = "";
    public bool EmailHigh { get; set; } = true; public bool EmailLow { get; set; } = true;
    public bool Reminders
    {
        get; set;
    }
    public int ReminderMinutes { get; set; } = 30; public bool RecoveryEmail
    {
        get; set;
    }
    [JsonIgnore, Browsable(false)] public bool Mapped => TemperatureOffset.HasValue;
    public override string ToString() => Name;
}
public sealed class Settings
{
    public bool Simulation { get; set; } = true; public string Site { get; set; } = "Medical refrigerator monitoring";
    public bool AudibleAlarm { get; set; } = true; public bool DailyBackup { get; set; } = false; public string BackupFolder { get; set; } = ""; public int RetentionDays { get; set; } = 30;
    public bool MailEnabled { get; set; } = false;
    public bool AutomaticAlerts { get; set; } = false;
    public string MailSecurity { get; set; } = "StartTls";
    [Browsable(false)] public string MailPassword { get; set; } = "";
    public string TestRecipient { get; set; } = "";
    public string StandardMessage { get; set; } = "Please check the temperature monitoring notification below.";
    public string SmtpHost { get; set; } = ""; public int SmtpPort { get; set; } = 587; public bool SslOnConnect { get; set; } = false;
    public string SmtpUsername { get; set; } = ""; [Browsable(false)] public string ProtectedPassword { get; set; } = "";
    public string SenderEmail { get; set; } = ""; public string SenderName { get; set; } = "Watchdog TM";
    [Browsable(false)] public string PasswordHash { get; set; } = "";
    [Browsable(false)] public string LicenseJson { get; set; } = "";
    [Browsable(false)] public List<Guid> CommissionedSensors { get; set; } = [];
}
public sealed class Configuration
{
    public List<Controller> Controllers { get; set; } = []; public List<Sensor> Sensors { get; set; } = []; public Settings Settings { get; set; } = new();
}
public sealed record Reading(DateTimeOffset At, double? Temperature, double? HighLimit, bool? High, bool? Low, bool Fault, string Quality);
public sealed record Sample(Guid SensorId, DateTimeOffset At, double? Temperature, string Quality);
public sealed record AlarmRow(long Id, string Sensor, string Kind, string Observed, string Acknowledged, string Cleared, string LastObserved);
public sealed record EmailRow(long Id, string Sensor, string Kind, string Status, int Attempts, string NextAttempt, string Error);
public sealed class ControllerState
{
    public string Status = "Offline"; public DateTimeOffset? LastSuccess; public string Error = "";
}
public static class Rules
{
    public static void Validate(Configuration c)
    {
        if (c.Controllers.Select(x => x.Id).Distinct().Count() != c.Controllers.Count || c.Sensors.Select(x => x.Id).Distinct().Count() != c.Sensors.Count)
            throw new Exception("Duplicate permanent IDs.");
        foreach (var p in c.Controllers)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || p.PollSeconds < 1 || p.PollSeconds > 3600 || p.TimeoutMs < 100 || p.TimeoutMs > 10000 || p.Retries < 0 || p.Retries > 3 || p.TcpPort < 1 || p.TcpPort > 65535 || p.UnitId == 0 || p.UnitId > 247)
                throw new Exception("Controller settings are outside supported bounds.");
            if (p.Protocol == Protocol.RTU && (p.BaudRate < 1200 || p.DataBits < 7 || p.DataBits > 8 || p.StopBits == System.IO.Ports.StopBits.None || string.IsNullOrWhiteSpace(p.ComPort)))
                throw new Exception("Invalid serial settings.");
        }
        foreach (var g in c.Controllers.Where(x => x.Enabled && x.Protocol == Protocol.RTU).GroupBy(x => x.ComPort.Trim().ToUpperInvariant()))
        {
            if (g.Select(x => x.SerialKey).Distinct().Count() > 1)
                throw new Exception("Controllers sharing a COM port must have matching serial settings.");
            if (g.Select(x => x.UnitId).Distinct().Count() != g.Count())
                throw new Exception("Each slave on a COM port needs a unique device address.");
        }
        foreach (var s in c.Sensors)
        {
            if ((s.SoftwareHighLimit.HasValue && !double.IsFinite(s.SoftwareHighLimit.Value)) ||
                (s.SoftwareLowLimit.HasValue && !double.IsFinite(s.SoftwareLowLimit.Value)) ||
                (s.SoftwareHighLimit.HasValue && s.SoftwareLowLimit.HasValue && s.SoftwareLowLimit >= s.SoftwareHighLimit))
                throw new Exception("The low alarm limit must be below the high alarm limit. Enter finite values, or leave a limit blank to disable it.");
            if (!c.Controllers.Any(x => x.Id == s.ControllerId) || string.IsNullOrWhiteSpace(s.Name) || s.SampleSeconds < 1 || s.SampleSeconds > 86400 || !double.IsFinite(s.Multiplier) || s.Multiplier == 0 || !double.IsFinite(s.Offset) || s.Decimals < 0 || s.Decimals > 6)
                throw new Exception("Sensor settings are incomplete or outside supported bounds.");
            if (s.Format is ValueFormat.Float32 or ValueFormat.Int32 or ValueFormat.UInt32 && s.TemperatureOffset == 65535)
                throw new Exception("A two-register value cannot start at offset 65535.");
        }
        if (c.Settings.RetentionDays < 1 || c.Settings.RetentionDays > 3650 || c.Settings.SmtpPort < 1 || c.Settings.SmtpPort > 65535)
            throw new Exception("Invalid backup retention or SMTP port.");
    }
    public static double Decode(ushort[] r, ValueFormat f, ByteOrder order, double multiplier, double offset)
    {
        byte[] b = r.SelectMany(v => new[] { (byte)(v >> 8), (byte)v }).ToArray();
        if (order is ByteOrder.BADC or ByteOrder.DCBA)
        for (int i = 0; i < b.Length; i += 2)
            (b[i], b[i + 1]) = (b[i + 1], b[i]);
        if (b.Length == 4 && order is ByteOrder.CDAB or ByteOrder.DCBA)
            b = [b[2], b[3], b[0], b[1]];
        uint raw = 0;
        foreach (var v in b)
            raw = (raw << 8) | v;
        double value = f switch
        {
            ValueFormat.Int16 => unchecked((short)raw),
            ValueFormat.UInt16 => raw,
            ValueFormat.Int32 => unchecked((int)raw),
            ValueFormat.UInt32 => raw,
            _ => BitConverter.Int32BitsToSingle(unchecked((int)raw))
        };
        value = value * multiplier + offset;
        if (!double.IsFinite(value))
            throw new IOException("Invalid non-finite temperature.");
        return value;
    }
    public static Configuration Demo()
    {
        var c = new Configuration();
        for (int p = 0; p < 3; p++)
        {
            var ctrl = new Controller { Name = $"DEMO PLC {p + 1}", Protocol = Protocol.Simulation };
            c.Controllers.Add(ctrl);
            for (int n = 0; n < 6; n++)
                c.Sensors.Add(new Sensor { ControllerId = ctrl.Id, Name = $"Refrigerator {p * 6 + n + 1:00}", Location = "SIMULATION — example mapping only", TemperatureOffset = (ushort)n, HighAlarmOffset = (ushort)n, HighLimitOffset = (ushort)(100 + n), Multiplier = .1, LimitMultiplier = .1 });
        }
        return c;
    }
}


// Addresses are deliberately unset until verified against the delivered PLC project.
public sealed class PlcHistoryLayout
{
 public bool Enabled {get;set;}
 public string Verification {get;set;}="";
 public int Capacity {get;set;}=300;
 public int SyncSeconds {get;set;}=600;
 public int RecordWords {get;set;}
 public ushort? HeaderAddress {get;set;}
 public int HeaderWords {get;set;}
 public int SequenceOffset {get;set;}=-1;
 public int CountOffset {get;set;}=-1;
 public int PositionOffset {get;set;}=-1;
 public int StatusOffset {get;set;}=-1;
 public ushort? BufferAddress {get;set;}
 public List<PlcHistorySegment> Segments {get;set;}=[];
 public int RecordSequenceOffset {get;set;}=-1;
 public int RecordStatusOffset {get;set;}=-1;
 public int IntervalOffset {get;set;}=-1;
 public int[] TimestampOffsets {get;set;}=[]; // year, month, day, hour, minute, second
 public int YearBase {get;set;}=2000;
 public int UtcOffsetMinutes {get;set;}
 public bool LowWordFirst {get;set;}=true;
 public int HealthyStatus {get;set;}=0;
 public List<PlcHistoryChannel> Channels {get;set;}=[];
}
public sealed class PlcHistoryChannel
{
 public Guid SensorId {get;set;}
 public int Offset {get;set;}=-1;
 public double Multiplier {get;set;}=0.1;
}

public sealed class PlcHistorySegment
{
 public int FirstRecord {get;set;}
 public int RecordCount {get;set;}
 public ushort Address {get;set;}
}
