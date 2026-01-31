using System.Collections.Concurrent;

namespace IamApi.Services;

public interface IConsoleLogService
{
    void AddLog(string level, string message, string? category = null);
    IEnumerable<ConsoleLogEntry> GetLogs(int count = 500, string? level = null);
    void Clear();
}

public sealed class ConsoleLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = string.Empty;
    public string? Category { get; set; }
}

public sealed class ConsoleLogService : IConsoleLogService
{
    private readonly ConcurrentQueue<ConsoleLogEntry> _logs = new();
    private const int MaxLogs = 2000;

    public void AddLog(string level, string message, string? category = null)
    {
        var entry = new ConsoleLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = message,
            Category = category
        };

        _logs.Enqueue(entry);

        // Trim old logs if we exceed max
        while (_logs.Count > MaxLogs && _logs.TryDequeue(out _)) { }
    }

    public IEnumerable<ConsoleLogEntry> GetLogs(int count = 500, string? level = null)
    {
        var logs = _logs.ToArray().Reverse();
        
        if (!string.IsNullOrEmpty(level))
        {
            logs = logs.Where(l => l.Level.Equals(level, StringComparison.OrdinalIgnoreCase));
        }

        return logs.Take(count);
    }

    public void Clear()
    {
        while (_logs.TryDequeue(out _)) { }
    }
}

/// <summary>
/// Logger provider that captures logs to the ConsoleLogService
/// </summary>
public sealed class ConsoleLogProvider : ILoggerProvider
{
    private readonly IConsoleLogService _consoleLogService;

    public ConsoleLogProvider(IConsoleLogService consoleLogService)
    {
        _consoleLogService = consoleLogService;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ConsoleLogger(_consoleLogService, categoryName);
    }

    public void Dispose() { }
}

public sealed class ConsoleLogger : ILogger
{
    private readonly IConsoleLogService _consoleLogService;
    private readonly string _categoryName;

    public ConsoleLogger(IConsoleLogService consoleLogService, string categoryName)
    {
        _consoleLogService = consoleLogService;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (exception != null)
        {
            message += $"\n{exception}";
        }

        var level = logLevel switch
        {
            LogLevel.Trace => "Trace",
            LogLevel.Debug => "Debug",
            LogLevel.Information => "Info",
            LogLevel.Warning => "Warn",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Critical",
            _ => "Info"
        };

        // Shorten category name for readability
        var category = _categoryName;
        var lastDot = category.LastIndexOf('.');
        if (lastDot > 0)
        {
            category = category[(lastDot + 1)..];
        }

        _consoleLogService.AddLog(level, message, category);
    }
}
