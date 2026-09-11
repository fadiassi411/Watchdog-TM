using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Watchdog.Licensing;
namespace Watchdog.TM;

public static class Security
{
    public static string Hash(string password)
    {
        if (password.Length < 10)
            throw new Exception("Use at least 10 characters.");
        var salt = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32));
    }
    public static bool Verify(string password, string hash)
    {
        try
        {
            var a = hash.Split(':');
            return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(a[1]), Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(a[0]), 600000, HashAlgorithmName.SHA256, 32));
        }
        catch { return false; }
    }
    public static string Protect(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    public static string Unprotect(string value) => value.Length == 0 ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    public static string InstallationId { get; } = Identity();
    static string Identity()
    {
        string? source = null;
        try
        {
            using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            source = key?.GetValue("MachineGuid")?.ToString();
        }
        catch { }
        source ??= $"{Environment.MachineName}|{Environment.OSVersion.Platform}|{Environment.Is64BitOperatingSystem}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"WatchdogEnergyManagement|{source.Trim()}"));
        return "WD-" + string.Join('-', Convert.ToHexString(hash[..15]).Chunk(5).Select(x => new string(x)));
    }
    public const string PublicKey = """
 -----BEGIN PUBLIC KEY-----
 MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEGc61/ruNWVnNOOmhewW8JXys8H6w
 201RRW26jMl9q5wYlA99veIVqABB72UgyYY17o7dX+l+xD3UhYrDGDiURw==
 -----END PUBLIC KEY-----
 """;
    public static TmLicensePayload License(string json, bool allowExpired = false, string? key = null, string? identity = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("No Watchdog TM license has been imported. Open Settings and Backup → Import / Upgrade license and select your signed TM license file before commissioning live monitoring. You can still use Test connection for a read-only temperature check.");
        TmLicenseDocument doc;
        try { doc = TmLicenseDocument.Parse(json); }
        catch (JsonException) { throw new InvalidDataException("This is not a valid Watchdog TM license file. Select the signed .wdlicense file supplied for this installation. Your existing license has not been replaced."); }
        if (!doc.TryVerify(key ?? PublicKey, out var p, out var error) || p == null)
            throw new Exception(error);
        if (p.InstallationId != (identity ?? InstallationId))
            throw new Exception("License belongs to a different installation.");
        if (p.IssuedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new Exception("License issue date is in the future.");
        if (p.MaximumTemperatureSensors < 1 || p.LicenseId == Guid.Empty || string.IsNullOrWhiteSpace(p.CustomerName) || p.ExpiresAtUtc <= p.IssuedAtUtc)
            throw new Exception("Invalid license fields.");
        if (!allowExpired && p.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new Exception("License expired. Existing commissioned monitoring continues.");
        return p;
    }
    public static void Capacity(Configuration c, Configuration? previous = null, string? verificationKey = null)
    {
        if (c.Settings.Simulation)
            return;
        var active = c.Sensors.Where(s => !s.Retired).ToList();
        TmLicensePayload p;
        try
        {
            p = License(c.Settings.LicenseJson, false, verificationKey);
        }
        catch { if (previous == null || active.Any(s => !previous.Sensors.Any(o => o.Id == s.Id && !o.Retired))) throw; return; }
        if (active.Count > p.MaximumTemperatureSensors)
            throw new Exception("Sensor capacity exceeded.");
    }
    public static string LicenseStatus(Configuration c)
    {
        try
        {
            var p = License(c.Settings.LicenseJson, true);
            int used = c.Sensors.Count(s => !s.Retired);
            return $"{p.CustomerName} | {used}/{p.MaximumTemperatureSensors} slots used | {Math.Max(0, p.MaximumTemperatureSensors - used)} available | " + (p.ExpiresAtUtc == null ? "Perpetual" : p.ExpiresAtUtc <= DateTimeOffset.UtcNow ? "EXPIRED — commissioned monitoring continues" : $"Expires {p.ExpiresAtUtc:yyyy-MM-dd}" + (p.ExpiresAtUtc < DateTimeOffset.UtcNow.AddDays(30) ? " — RENEW SOON" : ""));
        }
        catch { return "No valid TM license — simulation available; existing commissioned monitoring continues."; }
    }
}


