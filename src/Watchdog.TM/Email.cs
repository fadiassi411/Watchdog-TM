using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
namespace Watchdog.TM;

public sealed class EmailWorker(MonitorEngine engine, Func<Settings, string, string, string, CancellationToken, Task>? sender = null)
{
    public static async Task Send(Settings s, string recipients, string subject, string body, CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(s.SenderName, s.SenderEmail));
        foreach (var r in recipients.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            msg.To.Add(MailboxAddress.Parse(r));
        msg.Subject = subject;
        msg.Body = new TextPart("plain") { Text = body };
        using var client = new SmtpClient { Timeout = 10000 };
        await client.ConnectAsync(s.SmtpHost, s.SmtpPort, s.SslOnConnect ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        if (!string.IsNullOrWhiteSpace(s.SmtpUsername))
            await client.AuthenticateAsync(s.SmtpUsername, Security.Unprotect(s.ProtectedPassword), ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
    public async Task Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!engine.Config.Settings.Simulation)
                try
                {
                    engine.Store.QueueDueReminders(engine.Config, DateTimeOffset.UtcNow); await ProcessOne(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch { }
            await Task.Delay(2000, ct);
        }
    }
    public async Task ProcessOne(CancellationToken ct)
    {
        if (engine.Config.Settings.Simulation)
            return;
        long id;
        Guid sensorId;
        string kind, opened, closed, last;
        int attempts;
        using (var d = engine.Store.Open())
        {
            using var q = d.CreateCommand();
            q.CommandText = "SELECT e.id,e.sensor,e.kind,e.attempts,a.opened,COALESCE(a.closed,''),a.last_seen FROM email e JOIN alarms a ON a.id=e.episode WHERE e.status='Pending' AND e.next <= $now ORDER BY e.id LIMIT 1";
            q.Parameters.AddWithValue("$now", Store.Utc(DateTimeOffset.UtcNow));
            using var r = q.ExecuteReader();
            if (!r.Read())
                return;
            id = r.GetInt64(0);
            sensorId = Guid.Parse(r.GetString(1));
            kind = r.GetString(2);
            attempts = r.GetInt32(3);
            opened = r.GetString(4);
            closed = r.GetString(5);
            last = r.GetString(6);
        }
        var s = engine.Config.Sensors.FirstOrDefault(x => x.Id == sensorId);
        if (s == null || !s.EmailEnabled || (kind == "Reminder" && (!s.Reminders || closed != "")))
        {
            engine.Store.Write(d => Store.Run(d, "UPDATE email SET status='Cancelled — configuration or alarm changed' WHERE id=$0", id));
            return;
        }
        var value = engine.Display(s);
        var controller = engine.Config.Controllers.First(x => x.Id == s.ControllerId);
        var body = $"Watchdog TM V4.2.1 — {engine.Config.Settings.Site}\n{kind} notification\nSensor: {s.Name}\nLocation: {s.Location}\nController: {controller.Name}\nLatest reading: {value.Temperature?.ToString("F" + s.Decimals) ?? "Unavailable"} °C ({value.Quality})\nReading time: {(value.At == DateTimeOffset.MinValue ? "Unavailable" : value.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"))}\nCurrent high limit: {value.HighLimit?.ToString() ?? "Unavailable"} °C\nAlarm first observed: {DateTimeOffset.Parse(opened).ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}\nLast observed: {DateTimeOffset.Parse(last).ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}\nTimezone: {TimeZoneInfo.Local.Id}\n" + (closed == "" ? "No return to normal has been observed. Communication gaps may obscure transition times." : $"This alarm has since cleared. Recovery observed: {DateTimeOffset.Parse(closed).ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
        try
        {
            await (sender ?? Send)(engine.Config.Settings, s.Recipients, $"Watchdog TM — {(kind == "Recovery" ? "RETURN TO NORMAL" : "HIGH TEMPERATURE")} — {s.Name}", body, ct);
            engine.Store.Write(d => Store.Run(d, "UPDATE email SET status='Submitted to mail server',attempts=attempts+1,error='' WHERE id=$0", id));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { string error = ex is MailKit.Security.AuthenticationException ? "Authentication failed. Check SMTP credentials." : ex is SslHandshakeException ? "TLS certificate or handshake failed." : "SMTP submission failed or is uncertain. Check server, credentials and connection. Retrying can duplicate a message accepted before connection loss."; engine.Store.Write(d => Store.Run(d, "UPDATE email SET status=$0,attempts=attempts+1,next=$1,error=$2 WHERE id=$3", attempts >= 4 ? "Failed" : "Pending", Store.Utc(DateTimeOffset.UtcNow.AddSeconds(Math.Min(1800, 30 * Math.Pow(2, attempts)))), error, id)); }
    }
}


