using Watchdog.TM;

public static class Trends
{
    public sealed record Point(DateTimeOffset At, double? Average, double? Minimum, double? Maximum, long Valid, long Missing, long Simulated);
    public static object Read(Store store, Guid id, DateTimeOffset from, DateTimeOffset to, int seconds, CancellationToken ct)
    {
        var duration=(to-from).TotalSeconds;
        if(duration<=0 || duration>366*86400 || seconds<0 || seconds>86400)
            throw new ArgumentException("Choose a valid period up to one year and an interval up to one day.");
        var minimum=(int)Math.Ceiling(duration/1200);
        var interval=Math.Max(seconds==0?(int)Math.Ceiling(duration/600):seconds,Math.Max(1,minimum));
        using var db=store.Open(); using var q=db.CreateCommand();
        // Group in SQLite so a year of second-by-second history never fills browser memory.
        q.CommandText="""
            SELECT CAST((julianday(at)-julianday($from))*86400.0/$step + 0.00001 AS INTEGER) AS bucket,
              AVG(CASE WHEN quality IN ('VALID','SIMULATION / VALID') THEN value END),
              MIN(CASE WHEN quality IN ('VALID','SIMULATION / VALID') THEN value END),
              MAX(CASE WHEN quality IN ('VALID','SIMULATION / VALID') THEN value END),
              SUM(CASE WHEN quality IN ('VALID','SIMULATION / VALID') AND value IS NOT NULL THEN 1 ELSE 0 END),
              SUM(CASE WHEN quality NOT IN ('VALID','SIMULATION / VALID') OR value IS NULL THEN 1 ELSE 0 END),
              SUM(CASE WHEN quality LIKE 'SIMULATION / %' THEN 1 ELSE 0 END)
            FROM samples WHERE sensor=$id AND at >= $from AND at <= $to
            GROUP BY bucket ORDER BY bucket
            """;
        q.Parameters.AddWithValue("$from",from.UtcDateTime.ToString("O"));
        q.Parameters.AddWithValue("$to",to.UtcDateTime.ToString("O"));
        q.Parameters.AddWithValue("$id",id.ToString()); q.Parameters.AddWithValue("$step",interval);
        using var reader=q.ExecuteReader(); var points=new List<Point>();
        while(reader.Read()) { ct.ThrowIfCancellationRequested(); points.Add(new(from.AddSeconds(reader.GetInt64(0)*interval),reader.IsDBNull(1)?null:reader.GetDouble(1),reader.IsDBNull(2)?null:reader.GetDouble(2),reader.IsDBNull(3)?null:reader.GetDouble(3),reader.GetInt64(4),reader.GetInt64(5),reader.GetInt64(6))); }
        return new {from,to,intervalSeconds=interval,points};
    }
}
