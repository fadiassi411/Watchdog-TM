using System.Windows;
namespace Watchdog.TM;

public partial class App : Application
{
    Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mutex = new Mutex(true, @"Global\WatchdogTMMonitoring", out bool first);
        if (!first)
        {
            MessageBox.Show("Watchdog TM is already running.");
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) => { MessageBox.Show("The operation could not be completed. " + args.Exception.Message, "Watchdog TM"); args.Handled = true; };
        try
        {
            var data = Environment.GetEnvironmentVariable("WATCHDOG_TM_DATA") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WatchdogTM");
            var store = new Store(System.IO.Path.Combine(data, "watchdog.db"));
            var config = store.Load();
            if (config.Settings.PasswordHash.Length == 0)
            {
                var dialog = new PasswordDialog("Create initial administrator password (at least 10 characters)", true);
                if (dialog.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
                config.Settings.PasswordHash = Security.Hash(dialog.Value);
                store.Save(config, "Administrator", "Initial administrator created; demonstration configuration");
            }
            MainWindow = new MainWindow(store, config);
            MainWindow.Show();
        }
        catch (Exception ex) { MessageBox.Show("Watchdog TM could not start: " + ex.Message); Shutdown(); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        mutex?.Dispose();
        base.OnExit(e);
    }
}

