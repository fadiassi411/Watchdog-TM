using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;
using OxyPlot.Wpf;
namespace Watchdog.TM;

public sealed class MainWindow : Window
{
    readonly Store store; readonly MonitorEngine engine; Configuration config => engine.Config; bool admin = false, closing = false; int loginFailures; DateTimeOffset loginAfter; string user => admin ? "Administrator" : "Operator";
    readonly TabControl tabs = new(); readonly TextBlock banner = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), FontWeight = System.Windows.FontWeights.SemiBold }; readonly WrapPanel cards = new(); readonly ComboBox filter = new() { Width = 230 }; readonly Dictionary<Guid, (Border Border, TextBlock Text)> cardMap = []; readonly ComboBox controllers = new() { MinWidth = 230 }; readonly ListBox sensors = new() { Height = 350 }; readonly TextBlock connection = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    readonly ComboBox trendSensor = new() { Width = 220 }; readonly TextBox from = new() { Width = 170 }; readonly TextBox to = new() { Width = 170 }; readonly PlotView chart = new() { Height = 430 }; readonly TextBlock trendInfo = new() { Margin = new Thickness(10) }; readonly CheckBox limitLine = new() { Content = "Show current high limit", Margin = new Thickness(10) };
    readonly DataGrid alarms = new() { IsReadOnly = true, AutoGenerateColumns = true, Height = 250 }; readonly DataGrid emails = new() { IsReadOnly = true, AutoGenerateColumns = true, Height = 180 }; readonly ListBox audit = new() { Height = 170 }; readonly TextBlock license = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };
    CancellationTokenSource? export; readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) }; int ticks; bool showDemoControls;
    public MainWindow(Store store, Configuration c)
    {
        Theme.Apply(this); this.store = store;
        engine = new(c, store);
        Title = "Watchdog TM V4.2.1";
        Width = 1370;
        Height = 960;
        MinWidth = 760; ResizeMode=ResizeMode.CanResize;
        MinHeight = 480;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Background = Theme.Background;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var dock = new DockPanel();
        var header = new DockPanel { Background = Theme.Navy, Height = 70 };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(24,0,0,0) }; brand.Children.Add(new System.Windows.Controls.Image { Width=44,Height=44,Margin=new Thickness(0,0,12,0),Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Watchdog TM;component/watchdog-logo.png")) }); var brandText=new StackPanel(); brandText.Children.Add(new TextBlock { Text="WATCHDOG TEMPERATURE MONITORING", Foreground=Brushes.White,FontSize=17,FontWeight=System.Windows.FontWeights.Bold });brandText.Children.Add(new TextBlock{Text="Watch every degree.  •  V4.2.1",Foreground=Theme.Brush("#A9BDC8"),FontSize=11});brand.Children.Add(brandText);
        var exit=Btn("Exit",Close);DockPanel.SetDock(exit,Dock.Right);header.Children.Add(exit);var fit=Btn("Fit window",()=>{WindowState=WindowState.Normal;WindowSizing.Fit(this);});DockPanel.SetDock(fit,Dock.Right);header.Children.Add(fit);var login = Btn("Administrator / Sign out", Login);
        DockPanel.SetDock(login, Dock.Right);
        header.Children.Add(login);
        header.Children.Add(brand);
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        DockPanel.SetDock(banner, Dock.Top);
        banner.FontSize=12;banner.Margin=new Thickness(12,8,12,8);var statusBar=new Border{Child=banner,Background=Theme.Brush("#FFF2CD"),CornerRadius=new CornerRadius(7),Margin=new Thickness(26,14,26,0)};DockPanel.SetDock(statusBar,Dock.Top);dock.Children.Add(statusBar);
        tabs.Margin=new Thickness(22,8,22,0);var footer=new TextBlock{Text="Watchdog TM V4.2.1                                      MicroBrain by Fadi Assi",Foreground=Theme.Muted,FontSize=11,Margin=new Thickness(26,8,26,8)};DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);dock.Children.Add(tabs);
        Content = dock;
        Dashboard();
        ConfigurationPage();
        TrendsPage();
        AlarmsPage();
        SettingsPage();
        RefreshLists();
        engine.Start();
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Closing += async (_, e) => { if (closing) return; e.Cancel = true; if (MessageBox.Show(this,"Monitoring, recording and notifications stop when this application closes. Close Watchdog TM?", "Monitoring active", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; timer.Stop(); await engine.Stop(); closing = true; Close(); };
    }
    Button Btn(string title, Action action)
    {
        var b = new Button { Content = title, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3) };
        b.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Watchdog TM"); } };
        return b;
    }
    Button AsyncBtn(string title, Func<Task> action)
    {
        var b = new Button { Content = title, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3) };
        b.Click += async (_, _) => { b.IsEnabled = false; try { await action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Watchdog TM"); } finally { b.IsEnabled = true; } };
        return b;
    }
    static T Clone<T>(T o) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(o))!;
    void RequireAdmin()
    {
        if (!admin)
            throw new Exception("Sign in as Administrator to change configuration.");
    }
    void Login()
    {
        if (admin)
        {
            admin = false;
            return;
        }
        if (DateTimeOffset.UtcNow < loginAfter)
            throw new Exception("Please wait before another sign-in attempt.");
        var d = new PasswordDialog("Administrator password") { Owner = this };
        if (d.ShowDialog() == true)
        {
            if (!Security.Verify(d.Value, config.Settings.PasswordHash))
            {
                loginAfter = DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, Math.Pow(2, ++loginFailures)));
                throw new Exception("Incorrect password.");
            }
            admin = true;
            loginFailures = 0;
            store.Audit(user, "Administrator signed in");
        }
    }
    StackPanel Page(string title)
    {
        var p = new StackPanel { Margin = new Thickness(4,8,4,14) };p.Children.Add(Theme.Heading(title));p.Children.Add(Theme.Note(title switch {"Dashboard"=>"Live temperature readings and communication status. Alarms are not configured in this version.","Controllers and Sensors"=>"Set up a controller, then assign its temperature sensors.","Trends and Export"=>"Review recorded temperatures and export the selected period.","Alarms and History"=>"Review observed alarms, acknowledgements and email status.",_=>"Manage access, operating mode, licensing and local backups."}));
        tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        return p;
    }
    void Dashboard()
    {
        var p = Page("Dashboard");
        var bar = new WrapPanel();
        bar.Children.Add(new TextBlock { Text = "Controller", VerticalAlignment = System.Windows.VerticalAlignment.Center });
        bar.Children.Add(filter);
        filter.SelectionChanged += (_, _) => BuildCards();
        bar.Children.Add(Btn("Silence sound", () => engine.Silenced = true));bar.Children.Add(Btn("Demo controls",()=>{showDemoControls=!showDemoControls;BuildCards();}));
        p.Children.Add(bar);
        p.Children.Add(cards);
    }
    void BuildCards()
    {
        cards.Children.Clear();
        cardMap.Clear();
        foreach (var s in config.Sensors.Where(s => !s.Retired && (filter.SelectedItem is not Controller c || c.Id == s.ControllerId)))
        {
            var body = new StackPanel();
            var t = new TextBlock { Text = s.Name, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(2), MinHeight = 94 };
            body.Children.Add(t);
            var buttons = new WrapPanel();
            buttons.Children.Add(Btn("Trend", () => { trendSensor.SelectedItem = s; tabs.SelectedIndex = 2; _ = LoadTrend(); }));

            if (config.Settings.Simulation && showDemoControls)
            {
                var scenario = new ComboBox { Width = 130, ItemsSource = new[] { "Normal", "Negative", "Fault" }, SelectedItem = engine.Transport.Scenarios.GetValueOrDefault(s.Id, "Normal") };
                scenario.SelectionChanged += (_, _) => engine.Transport.Scenarios[s.Id] = (string)scenario.SelectedItem;
                buttons.Children.Add(scenario);
            }
            body.Children.Add(buttons);
            var border = new Border { Child = body, Width = Math.Max(190, ((ActualWidth > 0 ? ActualWidth : Width) - 235) / 6), MinHeight = 160, CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Margin = new Thickness(5), Background = Brushes.LightGray };
            cards.Children.Add(border);
            cardMap[s.Id] = (border, t);
        }
    }
    void Tick()
    {
        ticks++;
        int high = 0, offline = 0;
        foreach (var s in config.Sensors.Where(s => !s.Retired))
        {
            var r = engine.Display(s);
            if (r.High == true || r.Low == true)
                high++;
            if (r.Quality is "OFFLINE" or "STALE")
                offline++;
            if (!cardMap.TryGetValue(s.Id, out var card))
                continue;
            string alarm = r.High == true ? "HIGH TEMPERATURE" : r.Low == true ? "LOW TEMPERATURE" : "";
            string status = r.Quality == "VALID" ? (alarm.Length > 0 ? alarm : "READING OK") : r.Quality + (alarm.Length > 0 ? " • Last known " + alarm : "");
            bool valid = r.Quality == "VALID";
            card.Text.Text = $"{s.Name}\n{(r.Temperature?.ToString("F" + s.Decimals) ?? "—")} °C{(!valid && r.Temperature.HasValue ? " (last known)" : "")}\n{status}\n{(r.At == DateTimeOffset.MinValue ? "No successful reading" : r.At.ToLocalTime().ToString("HH:mm:ss  dd MMM zzz"))}";
            var lines = card.Text.Text.Split('\n');
            card.Text.Inlines.Clear();
            for (int n = 0; n < lines.Length; n++)
            {
                card.Text.Inlines.Add(new Run(lines[n]) { FontSize = n == 1 ? 29 : n == 0 ? 15 : 11, FontWeight = n < 2 ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal });
                if (n < lines.Length - 1)
                    card.Text.Inlines.Add(new LineBreak());
            }
            card.Border.Background = r.Quality is "DISABLED" or "RETIRED" ? Brushes.LightGray : !valid ? new SolidColorBrush(Color.FromRgb(251, 216, 147)) : alarm.Length > 0 ? new SolidColorBrush(ticks % 2 == 0 ? Color.FromRgb(255, 154, 154) : Color.FromRgb(255, 205, 205)) : new SolidColorBrush(Color.FromRgb(232, 245, 237));
        }
        banner.Text = $"{(config.Settings.Simulation ? "DEMONSTRATION MODE — live monitoring is off" : "LIVE MONITORING")}   |   {user}   |   {high} alarms   |   {offline} offline/stale" + (store.Fault.Length > 0 ? "\n" + store.Fault : "") + (engine.OperationalFault.Length > 0 ? "\n" + engine.OperationalFault : "");
        if(!config.Settings.Simulation)banner.Text+="\n"+Security.LicenseStatus(config);
        if (high > 0 && !engine.Silenced && config.Settings.AudibleAlarm && ticks % 4 == 0)
            System.Media.SystemSounds.Exclamation.Play();
        if (controllers.SelectedItem is Controller ctrl)
        {
            var state = engine.States.GetValueOrDefault(ctrl.Id);
            connection.Text = $"{state?.Status ?? "Offline"} | Last success: {state?.LastSuccess?.ToLocalTime().ToString() ?? "Never"}\n{state?.Error}";if(config.Settings.Simulation)connection.Text="Demonstration mode — automatic live polling is off. Test connection performs one real temperature read.";if(!config.Sensors.Any(s=>s.ControllerId==ctrl.Id&&!s.Retired&&s.Enabled))connection.Text="No active sensors. Add a sensor, or reactivate an existing one, to monitor this controller.";
        }
        if (ticks % 5 == 0 && tabs.SelectedIndex == 3)
            RefreshHistory();
    }
    void ConfigurationPage()
    {
        var p = Page("Controllers and Sensors");
        p.Children.Add(new TextBlock { Text = "Configure controllers first, then their sensors. Disable preserves history; retire releases a license slot.", Margin = new Thickness(6) });
        p.Children.Add(controllers);
        controllers.SelectionChanged += (_, _) => RefreshSensors();
        var bar = new WrapPanel();
        bar.Children.Add(AsyncBtn("Add controller", async () => { RequireAdmin(); var c = Clone(config); var x = new Controller(); if (new Editor(x, "Add controller") { Owner = this }.ShowDialog() == true) { c.Controllers.Add(x); await Save(c, "Added controller " + x.Name); } }));
        bar.Children.Add(AsyncBtn("Edit controller", async () => { RequireAdmin(); if (controllers.SelectedItem is not Controller x) return; var c = Clone(config); if (new Editor(c.Controllers.First(p => p.Id == x.Id), "Edit controller") { Owner = this }.ShowDialog() == true) await Save(c, "Edited controller " + x.Name); }));
        bar.Children.Add(AsyncBtn("Disable / Enable", async () => { RequireAdmin(); if (controllers.SelectedItem is not Controller x) return; var c = Clone(config); var p = c.Controllers.First(p => p.Id == x.Id); p.Enabled = !p.Enabled; await Save(c, "Controller enabled state changed: " + p.Name); }));
        bar.Children.Add(AsyncBtn("Test connection", async () => { if (controllers.SelectedItem is not Controller x) return; if(x.Protocol==Protocol.Simulation){MessageBox.Show("This is a demonstration controller. Choose Modbus RTU or TCP in Controller setup to test real hardware.");return;}var s=config.Sensors.FirstOrDefault(s=>s.ControllerId==x.Id&&!s.Retired&&s.Enabled&&s.TemperatureOffset.HasValue);if(s==null)throw new Exception("Add an enabled, non-retired sensor and enter its verified temperature register first. Alarm addresses are not needed for a read-only connection test.");if(MessageBox.Show($"Read temperature register {s.TemperatureOffset} on {x.Name} now?\nThis test reads real hardware once. It does not write any register or enable live monitoring.","Test controller communication",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;var temperature=await engine.Transport.ReadTemperature(x,s,CancellationToken.None);MessageBox.Show($"Communication successful\n{s.Name}: {temperature:F2} °C\nProtocol register: {s.TemperatureOffset}\n"+(config.Settings.Simulation?"Demonstration mode is still on. Use Settings to commission live monitoring.":"Check the reading against the reference instrument.")); }));
        bar.Children.Add(Btn("Simulate disconnect / reconnect", () => { if (!config.Settings.Simulation) return; if (controllers.SelectedItem is Controller c) engine.Transport.Scenarios[c.Id] = engine.Transport.Scenarios.GetValueOrDefault(c.Id) == "Offline" ? "Normal" : "Offline"; }));
        p.Children.Add(bar);
        p.Children.Add(connection);
        sensors.ItemTemplate=(DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><StackPanel><TextBlock Text=\"{Binding Name}\" FontWeight=\"SemiBold\"/><StackPanel Orientation=\"Horizontal\"><TextBlock Text=\"{Binding Location}\" Foreground=\"#6A7884\"/><TextBlock Text=\"  •  Register \" Foreground=\"#6A7884\"/><TextBlock Text=\"{Binding TemperatureOffset}\" Foreground=\"#6A7884\"/></StackPanel></StackPanel></DataTemplate>");p.Children.Add(sensors);
        var sb = new WrapPanel();
        sb.Children.Add(AsyncBtn("Add sensor", async () => { RequireAdmin(); if (controllers.SelectedItem is not Controller x) return; var c = Clone(config); var s = new Sensor { ControllerId = x.Id }; if (new Editor(s, "Add sensor — live offsets start blank") { Owner = this }.ShowDialog() == true) { c.Sensors.Add(s); await Save(c, "Added sensor " + s.Name); } }));
        sb.Children.Add(AsyncBtn("Edit sensor", async () => { RequireAdmin(); if (sensors.SelectedItem is not Sensor s) return; var c = Clone(config); var updated = c.Sensors.First(x => x.Id == s.Id); if (new Editor(updated, "Edit sensor — ID " + s.Id) { Owner = this }.ShowDialog() == true) await Save(c, $"Sensor changed: {s.Name} → {updated.Name} ({s.Id})"); }));
        sb.Children.Add(AsyncBtn("Retire / Reactivate", async () => { RequireAdmin(); if (sensors.SelectedItem is not Sensor s) return; var c = Clone(config); var updated = c.Sensors.First(x => x.Id == s.Id); updated.Retired = !updated.Retired; await Save(c, $"Sensor retirement changed: {s.Id}"); }));
        sb.Children.Add(AsyncBtn("Move to controller", async () => { RequireAdmin(); if (sensors.SelectedItem is not Sensor s) return; var choice = Pick("Choose destination controller", config.Controllers.Cast<object>()); if (choice is Controller target) { var c = Clone(config); c.Sensors.First(x => x.Id == s.Id).ControllerId = target.Id; await Save(c, "Moved sensor " + s.Id); } }));
        p.Children.Add(sb);
    }
    object? Pick(string title, IEnumerable<object> values)
    {
        var w = new Window { Title = title, Width = 450, Height = 180, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var p = new StackPanel { Margin = new Thickness(15) };
        var combo = new ComboBox { ItemsSource = values };
        p.Children.Add(combo);
        p.Children.Add(Btn("Select", () => w.DialogResult = combo.SelectedItem != null));
        w.Content = p;
        return w.ShowDialog() == true ? combo.SelectedItem : null;
    }
    async Task Save(Configuration c, string action)
    {
        Rules.Validate(c);
        Security.Capacity(c, config);
        if (config.Settings.Simulation != c.Settings.Simulation && c.Settings.Simulation == false)
        {
            throw new Exception("Use Start live monitoring to leave simulation.");
        }
        store.Save(c, user, action);
        await engine.Replace(c);
        RefreshLists();
    }
    void RefreshLists()
    {
        var id = (controllers.SelectedItem as Controller)?.Id;
        controllers.ItemsSource = config.Controllers;
        controllers.SelectedItem = config.Controllers.FirstOrDefault(c => c.Id == id) ?? config.Controllers.FirstOrDefault();
        filter.ItemsSource = new object[] { "All controllers" }.Concat(config.Controllers.Cast<object>());
        filter.SelectedIndex = 0;
        trendSensor.ItemsSource = config.Sensors;
        trendSensor.SelectedIndex = config.Sensors.Count > 0 ? 0 : -1;
        license.Text = $"Watchdog TM V4.2.1\nInstallation ID: {Security.InstallationId}\n{Security.LicenseStatus(config)}";
        BuildCards();
    }
    void RefreshSensors()
    {
        sensors.ItemsSource = controllers.SelectedItem is Controller c ? config.Sensors.Where(s => s.ControllerId == c.Id).ToList() : null;
    }
    void TrendsPage()
    {
        var p = Page("Trends and Export");
        var bar = new WrapPanel();
        bar.Children.Add(trendSensor);
        foreach (var (label, hours) in new[] { ("Last hour", 1), ("24 hours", 24), ("7 days", 168) })
            bar.Children.Add(AsyncBtn(label, async () => { from.Text = DateTime.Now.AddHours(-hours).ToString("yyyy-MM-dd HH:mm:ss"); to.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); await LoadTrend(); }));
        p.Children.Add(bar);
        var range = new WrapPanel();
        range.Children.Add(new TextBlock { Text = "From (local)" });
        range.Children.Add(from);
        range.Children.Add(new TextBlock { Text = "To (local)" });
        range.Children.Add(to);
        from.Text = DateTime.Now.AddHours(-24).ToString("yyyy-MM-dd HH:mm:ss");
        to.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        range.Children.Add(AsyncBtn("Load range", LoadTrend));

        p.Children.Add(range);
        p.Children.Add(chart);
        p.Children.Add(trendInfo);
        var ex = new WrapPanel();
        ex.Children.Add(AsyncBtn("Export all samples to Excel", Export));
        ex.Children.Add(Btn("Cancel export", () => export?.Cancel()));
        p.Children.Add(ex);
        p.Children.Add(new TextBlock { Text = "Drag to pan • Mouse wheel to zoom • Click a plotted point for timestamp and value • Missing samples create gaps. All range boundaries use local time: " + TimeZoneInfo.Local.Id, TextWrapping = TextWrapping.Wrap });
    }
    (DateTimeOffset, DateTimeOffset) Range()
    {
        if (!DateTime.TryParse(from.Text, out var f) || !DateTime.TryParse(to.Text, out var t) || f > t)
            throw new Exception("Enter a valid date/time range.");
        if (TimeZoneInfo.Local.IsInvalidTime(f) || TimeZoneInfo.Local.IsInvalidTime(t) || TimeZoneInfo.Local.IsAmbiguousTime(f) || TimeZoneInfo.Local.IsAmbiguousTime(t))
            throw new Exception("Choose unambiguous local range boundaries outside daylight-saving transitions.");
        return (new DateTimeOffset(f), new DateTimeOffset(t));
    }
    async Task LoadTrend()
    {
        try
        {
            if (trendSensor.SelectedItem is not Sensor s)
                return;
            var (f, t) = Range();
            var rows = await Task.Run(() => store.Samples(s.Id, f, t));
            var model = new PlotModel { Title = s.Name + " — °C", Subtitle = TimeZoneInfo.Local.Id };
            model.Axes.Add(new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "dd MMM HH:mm" });
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Temperature °C" });
            var line = new LineSeries { Title = s.Name, Color = OxyColor.FromRgb(22, 117, 140), StrokeThickness = 2, TrackerFormatString = "{0}\n{2:yyyy-MM-dd HH:mm:ss}\n{4:F2} °C" }; // Extrema per bucket preserve excursions; every missing sample remains a gap.
            int bucket = Math.Max(1, rows.Count / 6000);
            for (int i = 0; i < rows.Count; i += bucket)
            {
                var group = rows.Skip(i).Take(bucket).ToList();
                if (group.Any(x => !x.Temperature.HasValue))
                {
                    line.Points.Add(DataPoint.Undefined);
                }
                else
                foreach (var x in group.OrderBy(x => x.Temperature).Take(1).Concat(group.OrderByDescending(x => x.Temperature).Take(1)).Distinct().OrderBy(x => x.At))
                    line.Points.Add(new(DateTimeAxis.ToDouble(x.At.LocalDateTime), x.Temperature!.Value));
            }
            model.Series.Add(line);
            if (limitLine.IsChecked == true && engine.Display(s).HighLimit is double high)
                model.Annotations.Add(new LineAnnotation { Type = LineAnnotationType.Horizontal, Y = high, Text = "Current high limit", Color = OxyColors.Red });
            chart.Model = model;
            trendInfo.Text = $"{rows.Count:N0} stored samples • {rows.Count(x => !x.Temperature.HasValue):N0} missing/invalid • {line.Points.Count:N0} plotted points";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
    async Task Export()
    {
        if (trendSensor.SelectedItem is not Sensor s)
            return;
        var (f, t) = Range();
        var d = new SaveFileDialog { Filter = "Excel workbook|*.xlsx", FileName = "WatchdogTM-temperatures.xlsx" };
        if (d.ShowDialog() != true)
            return;
        export = new();
        try
        {
            var c = config.Controllers.First(x => x.Id == s.ControllerId);
            int count = await Task.Run(() => Exporter.Export(store, s, c, f, t, d.FileName, export.Token));
            MessageBox.Show($"Exported {count:N0} stored samples.");
        }
        catch (OperationCanceledException) { MessageBox.Show("Export cancelled."); }
        finally { export.Dispose(); export = null; }
    }
    void AlarmsPage()
    {
        var p = Page("Alarms and History");
        p.Children.Add(new TextBlock { Text = "Historical alarms only. New software alarms will be configured in a later update." });
        p.Children.Add(alarms);
        p.Children.Add(Btn("Acknowledge selected alarm", () => { if (alarms.SelectedItem is AlarmRow a) { store.Ack(a.Id, user); RefreshHistory(); } }));
        p.Children.Add(new TextBlock { Text = "Email notifications — SMTP acceptance does not prove recipient delivery.", Margin = new Thickness(5, 15, 5, 5) });
        p.Children.Add(emails);
        p.Children.Add(Btn("Retry selected failed email", () => { if (emails.SelectedItem is EmailRow e && e.Status == "Failed") { store.Write(d => { Store.Run(d, "UPDATE email SET status='Pending',attempts=0,next=$0 WHERE id=$1", Store.Utc(DateTimeOffset.UtcNow), e.Id); Store.Audit(d, user, "Manual email retry " + e.Id); }); RefreshHistory(); } }));
        p.Children.Add(new TextBlock { Text = "Audit history", Margin = new Thickness(5, 15, 5, 5) });
        p.Children.Add(audit);
        p.Children.Add(Btn("Refresh history", RefreshHistory));
        RefreshHistory();
    }
    void RefreshHistory()
    {
        alarms.ItemsSource = store.Alarms(config);
        emails.ItemsSource = store.Emails(config);
        audit.ItemsSource = store.Audits();
    }
    void SettingsPage()
    {
        var p = Page("Settings and Backup");
        p.Children.Add(new TextBlock { Text = "Monitoring requires this computer and Watchdog TM to remain running.", FontSize = 18, Margin = new Thickness(8) });
        var b = new WrapPanel();
        b.Children.Add(AsyncBtn("Application / SMTP settings", async () => { RequireAdmin(); var c = Clone(config); if (new Editor(c.Settings, "Application and SMTP settings") { Owner = this }.ShowDialog() == true) await Save(c, "Application / SMTP settings changed"); }));
        b.Children.Add(AsyncBtn("Set SMTP password", async () => { RequireAdmin(); var d = new PasswordDialog("SMTP password (stored using Windows protection)") { Owner = this }; if (d.ShowDialog() == true) { var c = Clone(config); c.Settings.ProtectedPassword = Security.Protect(d.Value); await Save(c, "SMTP credential updated"); } }));
        b.Children.Add(AsyncBtn("Send test email", async () => { RequireAdmin(); var w = new Window { Title = "Explicit SMTP test", Width = 480, Height = 175, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner }; var panel = new StackPanel { Margin = new Thickness(12) }; var recipient = new TextBox(); panel.Children.Add(new TextBlock { Text = "Recipient for one real test email (also in simulation)" }); panel.Children.Add(recipient); panel.Children.Add(Btn("Send this test email", () => w.DialogResult = true)); w.Content = panel; if (w.ShowDialog() == true) { try { await EmailWorker.Send(config.Settings, recipient.Text, "Watchdog TM — explicit test", "This is an explicitly requested test from Watchdog TM V4.2.1.", CancellationToken.None); MessageBox.Show("Submitted to mail server."); } catch { MessageBox.Show("Test failed. Check SMTP connection, security mode, credentials and recipient."); } } }));
        b.Children.Add(Btn("Start with Windows", () => { RequireAdmin(); using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); key.SetValue("WatchdogTM", "\"" + Environment.ProcessPath + "\""); store.Audit(user, "Start with Windows enabled"); MessageBox.Show("Enabled for this Windows user."); }));
        b.Children.Add(Btn("Disable Windows startup", () => { RequireAdmin(); using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); key.DeleteValue("WatchdogTM", false); store.Audit(user, "Start with Windows disabled"); }));
        b.Children.Add(AsyncBtn("Change admin password", async () => { RequireAdmin(); var d = new PasswordDialog("New administrator password", true) { Owner = this }; if (d.ShowDialog() == true) { var c = Clone(config); c.Settings.PasswordHash = Security.Hash(d.Value); await Save(c, "Administrator password changed"); } }));
        p.Children.Add(Theme.Panel(b));
        var backup = new WrapPanel();
        backup.Children.Add(AsyncBtn("Back up database", async () => { RequireAdmin(); var d = new SaveFileDialog { Filter = "Watchdog database|*.db", FileName = $"WatchdogTM-{DateTime.Now:yyyyMMdd-HHmmss}.db" }; if (d.ShowDialog() == true) { await Task.Run(() => store.Backup(d.FileName)); store.Audit(user, "Manual backup completed"); MessageBox.Show("Database-consistent backup completed."); } }));
        backup.Children.Add(AsyncBtn("Restore backup", Restore));
        p.Children.Add(Theme.Heading("Backup and restore",17));p.Children.Add(Theme.Panel(backup));
        p.Children.Add(Theme.Heading("Operating mode and license",17));p.Children.Add(Theme.Panel(license));
        var lb = new WrapPanel();
        lb.Children.Add(Btn("Copy installation ID", () => Clipboard.SetText(Security.InstallationId)));
        lb.Children.Add(AsyncBtn("Import / Upgrade license", async () => { RequireAdmin(); var d = new OpenFileDialog { Filter = "Watchdog license|*.wdlicense|All files|*.*" }; if (d.ShowDialog() != true) return; var json = File.ReadAllText(d.FileName); var valid = Security.License(json); if (config.Sensors.Count(s => !s.Retired) > valid.MaximumTemperatureSensors) throw new Exception("License capacity is below the configured sensor count."); var c = Clone(config); c.Settings.LicenseJson = json; await Save(c, "Imported TM license " + valid.LicenseId); }));
        lb.Children.Add(AsyncBtn("Start live monitoring", Commission));
        lb.Children.Add(AsyncBtn("Switch to simulation", async () => { RequireAdmin(); if (MessageBox.Show("Stop live monitoring and enter simulation?", "Confirm mode change", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; var c = Clone(config); c.Settings.Simulation = true; await Save(c, "Switched to simulation"); }));
        lb.Children.Add(Btn("Customer guide", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory, "docs", "Customer-guide.html")) { UseShellExecute = true })));
        lb.Children.Add(Btn("About", () => { MessageBox.Show("Watchdog TM V4.2.1\nLocal refrigerator monitoring\nSimulator verification is separate from hardware commissioning.\nSee docs/Customer-guide.md supplied with this application."); }));
        p.Children.Add(lb);
    }
    async Task Commission()
    {
        RequireAdmin();
        var c = Clone(config);
        c.Settings.Simulation = false;
        Rules.Validate(c);
        Security.Capacity(c);
        if (c.Controllers.Any(x => x.Enabled && x.Protocol == Protocol.Simulation) || c.Sensors.Any(x => !x.Retired && !x.Mapped))
            throw new Exception("Choose RTU or TCP for enabled controllers and enter a temperature register for each active sensor.");
        if (MessageBox.Show("Start continuous read-only monitoring of the configured temperature registers? No PLC alarm or limit registers will be read or written. Software alarms are not configured yet.", "Live commissioning", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        c.Settings.CommissionedSensors = c.Sensors.Where(x => !x.Retired).Select(x => x.Id).ToList();
        store.Save(c, user, "Live commissioning authorized");
        await engine.Replace(c);
        RefreshLists();
    }
    async Task Restore()
    {
        RequireAdmin();
        var d = new OpenFileDialog { Filter = "Watchdog database|*.db" };
        if (d.ShowDialog() != true)
            return;
        var candidate = await Task.Run(() => store.InspectBackup(d.FileName));
        Rules.Validate(candidate);
        candidate.Settings.LicenseJson = config.Settings.LicenseJson;
        Security.Capacity(candidate);
        if (!candidate.Settings.Simulation)
        {
            Security.License(candidate.Settings.LicenseJson);
            if (candidate.Sensors.Any(x => !x.Retired && !x.Mapped))
                throw new Exception("Backup contains unmapped live sensors.");
        }
        if (MessageBox.Show("Replace all current settings and history with this backup? A safety backup will be saved first. SMTP credentials may need re-entry if this backup came from another Windows account.", "Confirm restore", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        await engine.Stop();
        try
        {
            await Task.Run(() => { store.Backup(store.Path + ".before-restore-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".db"); store.Restore(d.FileName); store.Save(candidate, user, "Backup restored; current activation preserved"); });
            await engine.Replace(candidate);
            admin = false;
            RefreshLists();
        }
        catch { await engine.Replace(store.Load()); throw; }
    }
}












