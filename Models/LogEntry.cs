namespace IamApi.Models;

public class LogEntry
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Message { get; set; } = string.Empty;
    public LogLevel Level { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Source { get; set; }
}

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Debug
}
