using System.Globalization;
using System.Security.Cryptography;
using Watchdog.Licensing;

return args.FirstOrDefault()?.ToLowerInvariant() switch
{
    "keygen" => GenerateKeys(args.Skip(1).ToArray()),
    "issue" => IssueLicense(args.Skip(1).ToArray()),
    "verify" => VerifyLicense(args.Skip(1).ToArray()),
    _ => ShowHelp()
};

static int GenerateKeys(string[] args)
{
    var values = Parse(args);
    var privatePath = Required(values, "private");
    var publicPath = Required(values, "public");
    if (File.Exists(privatePath) || File.Exists(publicPath))
        throw new InvalidOperationException("Refusing to overwrite an existing licensing key.");

    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(privatePath))!);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(publicPath))!);
    File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicPath, key.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine($"Created private key: {Path.GetFullPath(privatePath)}");
    Console.WriteLine($"Created public key:  {Path.GetFullPath(publicPath)}");
    Console.WriteLine("Back up the private key securely. Never commit or distribute it.");
    return 0;
}

static int IssueLicense(string[] args)
{
    var values = Parse(args);
    var privatePath = Required(values, "private");
    var customer = Required(values, "customer").Trim();
    var installation = Required(values, "installation").Trim().ToUpperInvariant();
    if (!System.Text.RegularExpressions.Regex.IsMatch(installation, @"^WD-(?:[0-9A-F]{5}-){5}[0-9A-F]{5}$"))
        throw new ArgumentException("Installation ID is not in the expected Watchdog format.");
    var output = Required(values, "out");
    var tm = values.GetValueOrDefault("product", "EM").Equals("TM", StringComparison.OrdinalIgnoreCase);
    if (!int.TryParse(Required(values, tm ? "sensors" : "meters"), out var meters) || (tm ? meters < 1 : meters < 50 || meters % 50 != 0))
        throw new ArgumentException("Paid meter capacity must be 50, 100, 150, and so on.");

    DateTimeOffset? expiry = null;
    if (values.TryGetValue("expires", out var expiresText))
    {
        if (!DateOnly.TryParseExact(expiresText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new ArgumentException("Expiry must use yyyy-MM-dd.");
        expiry = new DateTimeOffset(day.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
    }

    var payload = new LicensePayload(
        1,
        Guid.NewGuid(),
        customer,
        installation,
        values.GetValueOrDefault("edition", "Commercial"),
        meters,
        DateTimeOffset.UtcNow,
        expiry);
    var documentJson = tm ? TmLicenseDocument.Create(new TmLicensePayload(2, "WatchdogTM", values.TryGetValue("license-id", out var lid) ? Guid.Parse(lid) : payload.LicenseId, customer, installation, payload.Edition, meters, payload.IssuedAtUtc, expiry), File.ReadAllText(privatePath)).ToJson() : LicenseDocument.Create(payload, File.ReadAllText(privatePath)).ToJson();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    File.WriteAllText(output, documentJson);
    Console.WriteLine($"License: {Path.GetFullPath(output)}");
    Console.WriteLine($"Customer: {payload.CustomerName}");
    Console.WriteLine($"Installation: {payload.InstallationId}");
    Console.WriteLine($"Meter capacity: {payload.MaximumMeters}");
    Console.WriteLine($"License ID: {payload.LicenseId}");
    return 0;
}

static int VerifyLicense(string[] args)
{
    var values = Parse(args);
    var json = File.ReadAllText(Required(values, "license")); if (TmLicenseDocument.Parse(json).TryVerify(File.ReadAllText(Required(values, "public")), out var tm, out _) && tm is not null) { Console.WriteLine($"VALID TM SIGNATURE: {tm.CustomerName}; {tm.MaximumTemperatureSensors} sensors; {tm.InstallationId}; expiry {tm.ExpiresAtUtc}"); return 0; } var document = LicenseDocument.Parse(json);
    if (!document.TryVerify(File.ReadAllText(Required(values, "public")), out var payload, out var error) || payload is null)
    {
        Console.Error.WriteLine($"INVALID: {error}");
        return 2;
    }
    Console.WriteLine("VALID ECDSA SIGNATURE");
    Console.WriteLine($"Customer: {payload.CustomerName}");
    Console.WriteLine($"Installation: {payload.InstallationId}");
    Console.WriteLine($"Meter capacity: {payload.MaximumMeters}");
    Console.WriteLine($"Expires: {(payload.ExpiresAtUtc is null ? "Never" : payload.ExpiresAtUtc.Value.ToString("yyyy-MM-dd"))}");
    Console.WriteLine($"License ID: {payload.LicenseId}");
    return 0;
}

static Dictionary<string, string> Parse(string[] args)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--") || i + 1 >= args.Length)
            throw new ArgumentException("Every option must use --name value.");
        values[args[i][2..]] = args[i + 1];
    }
    return values;
}

static string Required(Dictionary<string, string> values, string name) =>
    values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing --{name}.");

static int ShowHelp()
{
    Console.WriteLine("Watchdog EM / TM offline license generator"); Console.WriteLine("TM: issue --product TM --sensors 18 --private <supplier.pem> --customer <name> --installation <id> --out <file.wdlicense> [--license-id <guid>] [--expires yyyy-MM-dd]");
    Console.WriteLine("  keygen --private <private.pem> --public <public.pem>");
    Console.WriteLine("  issue --private <private.pem> --customer <name> --installation <id> --meters <50|100|...> --out <file.wdlicense> [--edition Commercial] [--expires yyyy-MM-dd]");
    Console.WriteLine("  verify --public <public.pem> --license <file.wdlicense>");
    return 1;
}

