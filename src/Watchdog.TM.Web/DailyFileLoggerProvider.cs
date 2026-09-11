using System.Collections.Concurrent;



/// <summary>Small dependency-free daily log used by installed, unattended deployments.</summary>
public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly string directory;
    private readonly ConcurrentDictionary<string, DailyFileLogger> loggers = new();
    private readonly object writeLock = new();

    public DailyFileLoggerProvider(string directory)
    {
        this.directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) =>
        loggers.GetOrAdd(categoryName, name => new DailyFileLogger(name, directory, writeLock));

    public void Dispose() => loggers.Clear();

    private sealed class DailyFileLogger(string category, string directory, object writeLock) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {category}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            try
            {
                lock (writeLock)
                {
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory, $"watchdog-tm-{DateTime.Now:yyyyMMdd}.log"),
                        line + Environment.NewLine);
                    DeleteExpiredLogs(directory);
                }
            }
            catch
            {
                // Logging must never terminate metering or the web host.
            }
        }

        private static void DeleteExpiredLogs(string directory)
        {
            foreach (var file in new DirectoryInfo(directory).GetFiles("watchdog-tm-*.log")
                         .OrderByDescending(file => file.Name).Skip(30))
            {
                try { file.Delete(); } catch { }
            }
        }
    }
}

