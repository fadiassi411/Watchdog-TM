using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace Watchdog.TM;

public sealed class Store
{
    public string Path
    {
        get;
    }
    public string Fault { get; private set; } = ""; readonly object gate = new();
    public Store(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var db = Open();
        Run(db, "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS latest(sensor TEXT PRIMARY KEY, mode INTEGER, json TEXT); CREATE TABLE IF NOT EXISTS config(id INTEGER PRIMARY KEY, json TEXT NOT NULL); CREATE TABLE IF NOT EXISTS samples(sensor TEXT NOT NULL, at TEXT NOT NULL, value REAL, quality TEXT NOT NULL, PRIMARY KEY(sensor,at)); CREATE INDEX IF NOT EXISTS ix_samples ON samples(sensor,at); CREATE TABLE IF NOT EXISTS audit(id INTEGER PRIMARY KEY, at TEXT,user TEXT,action TEXT); CREATE TABLE IF NOT EXISTS alarms(id INTEGER PRIMARY KEY,sensor TEXT,kind TEXT,opened TEXT,ack TEXT,closed TEXT,last_seen TEXT); CREATE UNIQUE INDEX IF NOT EXISTS ix_episode ON alarms(sensor,kind) WHERE closed IS NULL; CREATE TABLE IF NOT EXISTS email(id INTEGER PRIMARY KEY,episode INTEGER,sensor TEXT,kind TEXT,slot TEXT,status TEXT,attempts INTEGER DEFAULT 0,next TEXT,error TEXT DEFAULT '',UNIQUE(episode,kind,slot));");
    }
    public SqliteConnection Open()
    {
        var d = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, DefaultTimeout = 5, Pooling = false }.ToString());
        d.Open();
        return d;
    }
    public static int Run(SqliteConnection d, string sql, params object?[] a)
    {
        using var q = d.CreateCommand();
        q.CommandText = sql;
        for (int i = 0; i < a.Length; i++)
            q.Parameters.AddWithValue("$" + i, a[i] ?? DBNull.Value);
        return q.ExecuteNonQuery();
    }
    public static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("O");
    public void Write(Action<SqliteConnection> a)
    {
        lock (gate)
        {
            try
            {
                using var d = Open();
                using var tx = d.BeginTransaction();
                a(d);
                tx.Commit();
                Fault = "";
            }
            catch { Fault = "RECORDING FAULT — database write failed. Check disk space and folder access."; throw; }
        }
    }
    public Configuration Load()
    {
        using var d = Open();
        using var q = d.CreateCommand();
        q.CommandText = "SELECT json FROM config WHERE id=1";
        return q.ExecuteScalar() is string s ? JsonSerializer.Deserialize<Configuration>(s)! : Rules.Demo();
    }
    public void Save(Configuration c, string user, string action)
    {
        Write(d => { Run(d, "INSERT INTO config VALUES(1,$0) ON CONFLICT(id) DO UPDATE SET json=$0", JsonSerializer.Serialize(c)); Audit(d, user, action); });
    }
    public static void Audit(SqliteConnection d, string user, string action) => Run(d, "INSERT INTO audit(at,user,action) VALUES($0,$1,$2)", Utc(DateTimeOffset.UtcNow), user, action);
    public void Audit(string user, string action) => Write(d => Audit(d, user, action));
    public void Latest(Guid id, Reading reading, bool simulation) => Write(d => Run(d, "INSERT INTO latest VALUES($0,$1,$2) ON CONFLICT(sensor) DO UPDATE SET mode=$1,json=$2", id.ToString(), simulation ? 1 : 0, JsonSerializer.Serialize(reading)));
    public Dictionary<Guid, Reading> Latest(bool simulation)
    {
        using var d = Open();
        using var q = d.CreateCommand();
        q.CommandText = "SELECT sensor,json FROM latest WHERE mode=$mode";
        q.Parameters.AddWithValue("$mode", simulation ? 1 : 0);
        using var r = q.ExecuteReader();
        var rows = new Dictionary<Guid, Reading>();
        while (r.Read())
            rows[Guid.Parse(r.GetString(0))] = JsonSerializer.Deserialize<Reading>(r.GetString(1))!;
        return rows;
    }
    public void Sample(Sample s) => Write(d => Run(d, "INSERT OR IGNORE INTO samples VALUES($0,$1,$2,$3)", s.SensorId.ToString(), Utc(s.At), s.Temperature, s.Quality));
    public DateTimeOffset? LastSample(Guid id)
    {
        using var d = Open();
        using var q = d.CreateCommand();
        q.CommandText = "SELECT MAX(at) FROM samples WHERE sensor=$id";
        q.Parameters.AddWithValue("$id", id.ToString());
        return q.ExecuteScalar() is string s ? DateTimeOffset.Parse(s) : null;
    }
    public List<Sample> Samples(Guid id, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var d = Open();
        using var q = d.CreateCommand();
        q.CommandText = "SELECT at,value,quality FROM samples WHERE sensor=$id AND at >= $from AND at <= $to ORDER BY at";
        q.Parameters.AddWithValue("$id", id.ToString());
        q.Parameters.AddWithValue("$from", Utc(from));
        q.Parameters.AddWithValue("$to", Utc(to));
        using var r = q.ExecuteReader();
        var rows = new List<Sample>();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(new(id, DateTimeOffset.Parse(r.GetString(0)), r.IsDBNull(1) ? null : r.GetDouble(1), r.GetString(2)));
        }
        return rows;
    }
    public bool Observe(Sensor s, string kind, bool active, DateTimeOffset at, bool simulation)
    {
        bool isNew = false;
        var storageKind = simulation ? "SIMULATION " + kind : kind;
        Write(d => { using var q = d.CreateCommand(); q.CommandText = "SELECT id,opened FROM alarms WHERE sensor=$id AND kind=$kind AND closed IS NULL"; q.Parameters.AddWithValue("$id", s.Id.ToString()); q.Parameters.AddWithValue("$kind", storageKind); using var r = q.ExecuteReader(); long? id = null; DateTimeOffset opened = at; if (r.Read()) { id = r.GetInt64(0); opened = DateTimeOffset.Parse(r.GetString(1)); } r.Close(); if (active) { if (id == null) { Run(d, "INSERT INTO alarms(sensor,kind,opened,last_seen) VALUES($0,$1,$2,$2)", s.Id.ToString(), storageKind, Utc(at)); q.CommandText = "SELECT last_insert_rowid()"; q.Parameters.Clear(); id = (long)q.ExecuteScalar()!; isNew = true; if (kind == "HIGH" && s.EmailEnabled) Queue(d, id.Value, s.Id, "Initial", "initial", at, simulation); } else Run(d, "UPDATE alarms SET last_seen=$0 WHERE id=$1", Utc(at), id.Value); if (kind == "HIGH" && s.EmailEnabled) Queue(d, id!.Value, s.Id, "Initial", "initial", at, simulation); if (kind == "HIGH" && s.EmailEnabled && s.Reminders && at - opened >= TimeSpan.FromMinutes(s.ReminderMinutes)) { var slot = ((long)(at - opened).TotalMinutes / s.ReminderMinutes).ToString(); Queue(d, id!.Value, s.Id, "Reminder", slot, at, simulation); } } else if (id != null) { Run(d, "UPDATE alarms SET closed=$0,last_seen=$0 WHERE id=$1", Utc(at), id.Value); Run(d, "UPDATE email SET status='Cancelled — alarm cleared' WHERE episode=$0 AND kind='Reminder' AND status='Pending'", id.Value); if (kind == "HIGH" && s.EmailEnabled && s.RecoveryEmail) Queue(d, id.Value, s.Id, "Recovery", "recovery", at, simulation); } });
        return isNew;
    }
    public void QueueDueReminders(Configuration c, DateTimeOffset now)
    {
        if (c.Settings.Simulation) return;
        Write(d => {
            using var q = d.CreateCommand();
            q.CommandText = "SELECT id,sensor,opened FROM alarms WHERE kind='HIGH' AND closed IS NULL";
            using var r = q.ExecuteReader();
            var active = new List<(long Id, Guid Sensor, DateTimeOffset Opened)>();
            while (r.Read()) active.Add((r.GetInt64(0), Guid.Parse(r.GetString(1)), DateTimeOffset.Parse(r.GetString(2))));
            r.Close();
            foreach (var a in active)
            {
                var s = c.Sensors.FirstOrDefault(s => s.Id == a.Sensor);
                if (s is null || s.Retired || !s.Enabled || !s.EmailEnabled || !s.Reminders || now - a.Opened < TimeSpan.FromMinutes(s.ReminderMinutes)) continue;
                var slot = ((long)(now - a.Opened).TotalMinutes / s.ReminderMinutes).ToString();
                Queue(d, a.Id, s.Id, "Reminder", slot, now, false);
            }
        });
    }
    static void Queue(SqliteConnection d, long episode, Guid sensor, string kind, string slot, DateTimeOffset now, bool simulation) => Run(d, "INSERT OR IGNORE INTO email(episode,sensor,kind,slot,status,next) VALUES($0,$1,$2,$3,$4,$5)", episode, sensor.ToString(), kind, slot, simulation ? "Simulation — not sent" : "Pending", Utc(now));
    public List<AlarmRow> Alarms(Configuration c)
    {
        using var d = Open();
        using var q = d.CreateCommand();
        q.CommandText = "SELECT id,sensor,kind,opened,ack,closed,last_seen FROM alarms ORDER BY id DESC LIMIT 2000";
        using var r = q.ExecuteReader();
        var rows = new List<AlarmRow>();
        while (r.Read())
            rows.Add(new(r.GetInt64(0), c.Sensors.FirstOrDefault(s => s.Id.ToString() == r.GetString(1))?.Name ?? r.GetString(1), r.GetString(2), Local(r, 3), Local(r, 4), Local(r, 5), Local(r, 6)));
        return rows;
    }
    static string Local(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : DateTimeOffset.Parse(r.GetString(i)).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
    public void Ack(long id, string user) => Write(d => { Run(d, "UPDATE alarms SET ack=COALESCE(ack,$0) WHERE id=$1", Utc(DateTimeOffset.UtcNow), id); Audit(d, user, $"Acknowledged alarm {id}; PLC alarm unchanged"); });
    public List<EmailRow> Emails(Configuration c)
    {
        using var d = Open();
        using var q = d.CreateCommand();
        q.CommandText = "SELECT id,sensor,kind,status,attempts,next,error FROM email ORDER BY id DESC LIMIT 1000";
        using var r = q.ExecuteReader();
        var rows = new List<EmailRow>();
        while (r.Read())
            rows.Add(new(r.GetInt64(0), c.Sensors.FirstOrDefault(s => s.Id.ToString() == r.GetString(1))?.Name ?? r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), Local(r, 5), r.GetString(6)));
        return rows;
    }
    public List<string> Audits()
    {
        using var d = Open();
        using var q = d.CreateCommand();
        q.CommandText = "SELECT at,user,action FROM audit ORDER BY id DESC LIMIT 2000";
        using var r = q.ExecuteReader();
        var rows = new List<string>();
        while (r.Read())
            rows.Add($"{Local(r, 0)} | {r.GetString(1)} | {r.GetString(2)}");
        return rows;
    }
    public void Backup(string target)
    {
        lock (gate)
        {
            if (System.IO.Path.GetFullPath(target).Equals(System.IO.Path.GetFullPath(Path), StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a different file for the backup.");
            using var source = Open();
            using var dest = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
            dest.Open();
            source.BackupDatabase(dest);
        }
    }
    public Configuration InspectBackup(string path)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        using var q = db.CreateCommand();
        q.CommandText = "PRAGMA integrity_check";
        if ((string?)q.ExecuteScalar() != "ok")
            throw new Exception("Backup integrity check failed.");
        q.CommandText = "SELECT json FROM config WHERE id=1";
        return JsonSerializer.Deserialize<Configuration>((string)q.ExecuteScalar()!)!;
    }
    public void Restore(string path)
    {
        lock (gate)
        {
            using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            source.Open();
            using var dest = Open();
            source.BackupDatabase(dest);
        }
    }
}


