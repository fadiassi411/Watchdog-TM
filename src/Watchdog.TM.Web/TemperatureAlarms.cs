using System.Text.Json;
using Watchdog.TM;

public sealed record TemperatureAlarmStatus(string Level, string Pending, string Episode, DateTimeOffset? Since, DateTimeOffset? AcknowledgedAt);

// Sample-driven state shared by the dashboard and mail queue. Browser refreshes never advance a delay.
public sealed class TemperatureAlarms
{
    readonly Store store;
    readonly object gate = new();
    public sealed class State
    {
        public string Level { get; set; } = "Normal";
        public string Pending { get; set; } = "";
        public DateTimeOffset? PendingSince { get; set; }
        public DateTimeOffset? Last { get; set; }
        public string Episode { get; set; } = "";
        public DateTimeOffset? Since { get; set; }
        public DateTimeOffset? AcknowledgedAt { get; set; }
    }
    public TemperatureAlarms(Store store)
    {
        this.store=store;
        store.Write(db=>Store.Run(db,"CREATE TABLE IF NOT EXISTS temperature_alarm_state(sensor TEXT PRIMARY KEY,json TEXT NOT NULL)"));
        // A restart cannot establish continuous readings during a pending delay.
        store.Write(db=>Store.Run(db,"UPDATE temperature_alarm_state SET json=json_set(json,'$.Pending','','$.PendingSince',NULL,'$.Last',NULL)"));
    }
    State Load(Guid id)
    {
        using var db=store.Open();using var q=db.CreateCommand();
        q.CommandText="SELECT json FROM temperature_alarm_state WHERE sensor=$id";q.Parameters.AddWithValue("$id",id.ToString());
        return q.ExecuteScalar() is string json ? JsonSerializer.Deserialize<State>(json)! : new();
    }
    void Save(Guid id,State value)=>store.Write(db=>Store.Run(db,"INSERT INTO temperature_alarm_state(sensor,json) VALUES($0,$1) ON CONFLICT(sensor) DO UPDATE SET json=excluded.json",id.ToString(),JsonSerializer.Serialize(value)));
    static TemperatureAlarmStatus Status(State s,string? level=null)=>new(level??s.Level,s.Pending,s.Episode,s.Since,s.AcknowledgedAt);
    public TemperatureAlarmStatus Observe(Sensor sensor,Reading reading,double maximumGapSeconds)
    {
        lock(gate)
        {
            var s=Load(sensor.Id);var raw=SoftwareAlarms.Evaluate(sensor,reading);
            if(raw=="Unavailable") {s.Pending="";s.PendingSince=null;s.Last=null;Save(sensor.Id,s);return Status(s,"Unavailable");}
            if(s.Last is {} last && reading.At<=last)return Status(s);
            if(s.Last is null || (reading.At-s.Last.Value).TotalSeconds>maximumGapSeconds) {s.Pending="";s.PendingSince=null;}
            s.Last=reading.At;
            var value=reading.Temperature!.Value;
            var desired=raw;
            if(s.Level=="High" && sensor.SoftwareHighLimit is double high && value>high-sensor.AlarmHysteresis && raw!="Low")desired="High";
            if(s.Level=="Low" && sensor.SoftwareLowLimit is double low && value<low+sensor.AlarmHysteresis && raw!="High")desired="Low";
            // Crossing directly into the opposite condition ends the old episode;
            // the new condition must independently satisfy its activation delay.
            if(desired is "High" or "Low" && s.Level!="Normal" && desired!=s.Level)
            {s.Level="Normal";s.Episode="";s.Since=null;s.AcknowledgedAt=null;}
            if(desired==s.Level || desired=="Normal")
            {
                s.Pending="";s.PendingSince=null;
                if(desired=="Normal" && s.Level!="Normal") {s.Level="Normal";s.Episode="";s.Since=null;s.AcknowledgedAt=null;}
            }
            else
            {
                if(s.Pending!=desired) {s.Pending=desired;s.PendingSince=reading.At;}
                if(reading.At-s.PendingSince!.Value>=TimeSpan.FromSeconds(sensor.AlarmDelaySeconds))
                {
                    s.Level=desired;s.Episode=Guid.NewGuid().ToString();s.Since=s.PendingSince;s.AcknowledgedAt=null;s.Pending="";s.PendingSince=null;
                }
            }
            Save(sensor.Id,s);return Status(s);
        }
    }
    public TemperatureAlarmStatus Current(Sensor sensor,Reading reading)
    {
        lock(gate)
        {
            var s=Load(sensor.Id);
            if(SoftwareAlarms.Evaluate(sensor,reading)=="Unavailable")return new("Unavailable","",s.Episode,s.Since,s.AcknowledgedAt);
            return Status(s);
        }
    }
    public bool Acknowledge(Guid id,string episode)
    {
        lock(gate)
        {
            var s=Load(id);
            if(s.Level=="Normal" || s.Episode!=episode || string.IsNullOrEmpty(episode))return false;
            if(s.AcknowledgedAt is null)
            {
                s.AcknowledgedAt=DateTimeOffset.UtcNow;
                store.Write(db=>{
                    Store.Run(db,"UPDATE temperature_alarm_state SET json=$0 WHERE sensor=$1",JsonSerializer.Serialize(s),id.ToString());
                    Store.Run(db,"UPDATE mail_outbox SET status='Cancelled',error='Alarm acknowledged' WHERE sensor=$0 AND kind LIKE '%reminder' AND status='Pending'",id.ToString());
                    Store.Audit(db,"Administrator",$"Temperature alarm acknowledged for {id}, episode {episode}");
                });
            }
            return true;
        }
    }
    public bool IsAcknowledged(Guid id) {lock(gate)return Load(id).AcknowledgedAt is not null;}
    public void Reset(Guid id) {lock(gate)store.Write(db=>Store.Run(db,"DELETE FROM temperature_alarm_state WHERE sensor=$0",id.ToString()));}
    public void ResetAll() {lock(gate)store.Write(db=>Store.Run(db,"DELETE FROM temperature_alarm_state"));}
}
