using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Watchdog.Licensing;

namespace WatchdogLicenseManager;

public sealed class MainForm : Form
{
    private static readonly Regex InstallationPattern = new(@"^WD-(?:[0-9A-F]{5}-){5}[0-9A-F]{5}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ComboBox product = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox customer = new();
    private readonly TextBox installation = new() { CharacterCasing = CharacterCasing.Upper };
    private readonly NumericUpDown meters = new() { Minimum = 50, Maximum = 10000, Increment = 50, Value = 50, ThousandsSeparator = true };
    private readonly ComboBox edition = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox hasExpiry = new() { Text = "License expires", AutoSize = true };
    private readonly DateTimePicker expiry = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy", Enabled = false };
    private readonly TextBox privateKey = new();
    private readonly TextBox outputFolder = new();
    private readonly Label result = new() { AutoSize = true, Visible = false, Padding = new Padding(12), MaximumSize = new Size(720, 0) };
    private Settings settings = new();

    public MainForm()
    {
        Text = "Watchdog License Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 700);
        Size = new Size(870, 770);
        BackColor = Color.FromArgb(244, 248, 250);
        Font = new Font("Segoe UI", 10F);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        BuildUi();
        LoadSettings();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Color.FromArgb(8, 46, 62) };
        header.Controls.Add(new Label { Text = "WATCHDOG LICENSE MANAGER", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 20F), AutoSize = true, Location = new Point(28, 17) });
        header.Controls.Add(new Label { Text = "Secure offline licensing for Watchdog EM and Watchdog TM", ForeColor = Color.FromArgb(183, 222, 229), AutoSize = true, Location = new Point(31, 62) });
        Controls.Add(header);
        Controls.Add(new Label { Dock = DockStyle.Bottom, Height = 34, Text = "MicroBrain by Fadi Assi  •  Private signing application", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(85, 106, 117) });

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(28, 22, 28, 18) };
        Controls.Add(scroll);
        scroll.BringToFront();
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, BackColor = Color.White, Padding = new Padding(24) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        scroll.Controls.Add(table);

        AddSection(table, "Create customer license", "Copy the Installation ID from Settings → License on the customer's main Watchdog PC.");
        product.Items.AddRange(["Watchdog EM", "Watchdog TM"]); product.SelectedIndex = 0; product.SelectedIndexChanged += (_, _) => { meters.Minimum = product.SelectedIndex == 1 ? 1 : 50; meters.Increment = product.SelectedIndex == 1 ? 1 : 50; meters.Value = product.SelectedIndex == 1 ? 18 : 50; }; AddField(table, "Product", product); AddField(table, "Customer name", customer);
        AddField(table, "Installation ID", installation);
        AddField(table, "Meter / sensor capacity", meters);
        edition.Items.AddRange(["Commercial", "Professional", "Enterprise"]);
        edition.SelectedIndex = 0;
        AddField(table, "Edition", edition);
        hasExpiry.CheckedChanged += (_, _) => expiry.Enabled = hasExpiry.Checked;
        expiry.MinDate = DateTime.Today.AddDays(1);
        expiry.Value = DateTime.Today.AddYears(1);
        var expiryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        expiryPanel.Controls.Add(hasExpiry);
        expiryPanel.Controls.Add(expiry);
        AddField(table, "Expiry", expiryPanel);

        AddSection(table, "Protected files", "Keep the private key only on this MicroBrain PC. Never send it to a customer or upload it to GitHub.");
        AddPathField(table, "Private key", privateKey, BrowseKey);
        AddPathField(table, "Save licenses in", outputFolder, BrowseOutput);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 18, 0, 0) };
        var generate = Button("Generate signed license", true, Generate);
        var verify = Button("Verify license", false, Verify);
        var open = Button("Open output folder", false, (_, _) => OpenFolder());
        buttons.Controls.Add(generate);
        buttons.Controls.Add(verify);
        buttons.Controls.Add(open);
        AddWide(table, buttons);
        result.Margin = new Padding(0, 14, 0, 0);
        AddWide(table, result);
    }

    private static void AddSection(TableLayoutPanel table, string title, string description)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Height = 80, Margin = new Padding(0, table.RowCount == 0 ? 0 : 16, 0, 4) };
        panel.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 15F), ForeColor = Color.FromArgb(8, 46, 62), AutoSize = true, Location = new Point(0, 0) });
        panel.Controls.Add(new Label { Text = description, ForeColor = Color.FromArgb(77, 101, 113), AutoSize = true, MaximumSize = new Size(700, 0), Location = new Point(1, 39) });
        AddWide(table, panel);
    }

    private static void AddField(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(30, 57, 70), Font = new Font("Segoe UI Semibold", 10F), Margin = new Padding(0, 7, 12, 9) }, 0, row);
        control.Dock = DockStyle.Fill;
        control.MinimumSize = new Size(0, 34);
        control.Margin = new Padding(0, 3, 0, 9);
        table.Controls.Add(control, 1, row);
        table.SetColumnSpan(control, 2);
    }

    private static void AddPathField(TableLayoutPanel table, string label, TextBox box, EventHandler browse)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(30, 57, 70), Font = new Font("Segoe UI Semibold", 10F), Margin = new Padding(0, 7, 12, 9) }, 0, row);
        box.Dock = DockStyle.Fill;
        box.MinimumSize = new Size(0, 34);
        box.Margin = new Padding(0, 3, 8, 9);
        table.Controls.Add(box, 1, row);
        var button = Button("Browse…", false, browse);
        button.Dock = DockStyle.Top;
        button.Margin = new Padding(0, 3, 0, 9);
        table.Controls.Add(button, 2, row);
    }

    private static void AddWide(TableLayoutPanel table, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 3);
    }

    private static Button Button(string text, bool primary, EventHandler click)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 42, Padding = new Padding(14, 5, 14, 5), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, BackColor = primary ? Color.FromArgb(0, 145, 137) : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(8, 76, 91), Font = new Font("Segoe UI Semibold", 10F) };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(176, 196, 204);
        button.Click += click;
        return button;
    }

    private void Generate(object? sender, EventArgs e)
    {
        try
        {
            var customerName = customer.Text.Trim();
            var installationId = installation.Text.Trim().ToUpperInvariant();
            var keyPath = privateKey.Text.Trim();
            var folder = outputFolder.Text.Trim();
            if (customerName.Length == 0) throw new InvalidOperationException("Enter the customer name.");
            if (!InstallationPattern.IsMatch(installationId)) throw new InvalidOperationException("Paste a valid Installation ID in WD-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX-XXXXX format.");
            if (product.SelectedIndex == 0 && meters.Value % 50 != 0) throw new InvalidOperationException("Meter capacity must be a block of 50.");
            if (!File.Exists(keyPath)) throw new FileNotFoundException("Select watchdog-license-private.pem.");
            if (folder.Length == 0) throw new InvalidOperationException("Select an output folder.");
            var expires = hasExpiry.Checked ? new DateTimeOffset(DateOnly.FromDateTime(expiry.Value).ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero) : (DateTimeOffset?)null;
            var payload = new LicensePayload(1, Guid.NewGuid(), customerName, installationId, edition.SelectedItem?.ToString() ?? "Commercial", decimal.ToInt32(meters.Value), DateTimeOffset.UtcNow, expires);
            var documentJson = product.SelectedIndex == 1 ? TmLicenseDocument.Create(new TmLicensePayload(2, "WatchdogTM", payload.LicenseId, customerName, installationId, payload.Edition, decimal.ToInt32(meters.Value), payload.IssuedAtUtc, expires), File.ReadAllText(keyPath)).ToJson() : LicenseDocument.Create(payload, File.ReadAllText(keyPath)).ToJson();
            Directory.CreateDirectory(folder);
            var safeName = Regex.Replace(customerName.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            if (safeName.Length == 0) safeName = "customer";
            var path = Path.Combine(folder, $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.wdlicense");
            File.WriteAllText(path, documentJson);
            settings = new Settings(keyPath, folder);
            SaveSettings();
            ShowResult($"License created successfully.\nCustomer: {payload.CustomerName}\nCapacity: {payload.MaximumMeters} capacity units\nLicense ID: {payload.LicenseId}\nFile: {path}", true);
            customer.Clear();
            installation.Clear();
        }
        catch (Exception ex) { ShowResult(ex.Message, false); }
    }

    private void Verify(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "Watchdog license (*.wdlicense)|*.wdlicense", Title = "Select a license to verify", InitialDirectory = Directory.Exists(outputFolder.Text) ? outputFolder.Text : null };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var publicPath = Path.Combine(Path.GetDirectoryName(privateKey.Text.Trim()) ?? "", "watchdog-license-public.pem");
            if (!File.Exists(publicPath)) throw new FileNotFoundException("The matching watchdog-license-public.pem was not found beside the private key.");
            var json = File.ReadAllText(dialog.FileName); if (TmLicenseDocument.Parse(json).TryVerify(File.ReadAllText(publicPath), out var tm, out _) && tm is not null) { ShowResult($"Valid Watchdog TM signature.\nCustomer: {tm.CustomerName}\nCapacity: {tm.MaximumTemperatureSensors} temperature sensors\nInstallation: {tm.InstallationId}\nExpiry: {tm.ExpiresAtUtc?.ToString() ?? "Perpetual"}", true); return; } var document = LicenseDocument.Parse(json);
            if (!document.TryVerify(File.ReadAllText(publicPath), out var payload, out var error) || payload is null) throw new CryptographicException(error);
            ShowResult($"Valid ECDSA license.\nCustomer: {payload.CustomerName}\nInstallation: {payload.InstallationId}\nCapacity: {payload.MaximumMeters} capacity units\nExpires: {(payload.ExpiresAtUtc is null ? "Never" : payload.ExpiresAtUtc.Value.ToString("dd MMM yyyy"))}", true);
        }
        catch (Exception ex) { ShowResult($"Verification failed: {ex.Message}", false); }
    }

    private void BrowseKey(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "PEM private key (*.pem)|*.pem", Title = "Select the Watchdog private signing key", FileName = File.Exists(privateKey.Text) ? privateKey.Text : "" };
        if (dialog.ShowDialog(this) == DialogResult.OK) privateKey.Text = dialog.FileName;
    }

    private void BrowseOutput(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "Select where generated licenses will be saved", UseDescriptionForTitle = true, SelectedPath = Directory.Exists(outputFolder.Text) ? outputFolder.Text : "" };
        if (dialog.ShowDialog(this) == DialogResult.OK) outputFolder.Text = dialog.SelectedPath;
    }

    private void OpenFolder()
    {
        try
        {
            if (outputFolder.Text.Trim().Length == 0) throw new InvalidOperationException("Select an output folder first.");
            Directory.CreateDirectory(outputFolder.Text.Trim());
            Process.Start(new ProcessStartInfo("explorer.exe", outputFolder.Text.Trim()) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowResult(ex.Message, false); }
    }

    private void ShowResult(string message, bool success)
    {
        result.Text = message;
        result.ForeColor = success ? Color.FromArgb(0, 91, 78) : Color.FromArgb(157, 39, 39);
        result.BackColor = success ? Color.FromArgb(224, 244, 240) : Color.FromArgb(255, 232, 232);
        result.Visible = true;
    }

    private void LoadSettings()
    {
        try { if (File.Exists(SettingsPath)) settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new(); } catch { settings = new(); }
        privateKey.Text = settings.PrivateKeyPath.Length > 0 ? settings.PrivateKeyPath : DefaultPrivateKey();
        outputFolder.Text = settings.OutputFolder.Length > 0 ? settings.OutputFolder : DefaultOutputFolder();
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string DefaultPrivateKey()
    {
        var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "watchdog-license-private.pem"));
        if (File.Exists(parent)) return parent;
        var beside = Path.Combine(AppContext.BaseDirectory, "watchdog-license-private.pem");
        return File.Exists(beside) ? beside : string.Empty;
    }

    private static string DefaultOutputFolder()
    {
        var parentKey = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "watchdog-license-private.pem"));
        return File.Exists(parentKey)
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "issued"))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Watchdog Licenses");
    }
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicroBrain", "WatchdogLicenseManager", "settings.json");
    private sealed record Settings(string PrivateKeyPath = "", string OutputFolder = "");
}

