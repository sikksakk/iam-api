using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IamApi.Models;
using IamApi.Services;

namespace IamApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<LogsController> _logger;
    private readonly IConsoleLogService _consoleLogService;

    public LogsController(IDataStore dataStore, ILogger<LogsController> logger, IConsoleLogService consoleLogService)
    {
        _dataStore = dataStore;
        _logger = logger;
        _consoleLogService = consoleLogService;
    }

    /// <summary>
    /// Get all logs, optionally filtered by job ID
    /// </summary>
    [HttpGet]
    public ActionResult<IEnumerable<LogEntry>> GetLogs([FromQuery] Guid? jobId = null, [FromQuery] string? source = null)
    {
        var logs = _dataStore.GetLogs(jobId);
        
        if (!string.IsNullOrEmpty(source))
        {
            logs = logs.Where(l => l.Source?.Equals(source, StringComparison.OrdinalIgnoreCase) == true);
        }
        
        return Ok(logs);
    }

    /// <summary>
    /// Get logs for a specific job
    /// </summary>
    [HttpGet("job/{jobId}")]
    public ActionResult<IEnumerable<LogEntry>> GetLogsByJob(Guid jobId)
    {
        var logs = _dataStore.GetLogsByJob(jobId);
        return Ok(logs);
    }

    /// <summary>
    /// Create a new log entry (for containers to report back)
    /// </summary>
    [HttpPost]
    public ActionResult<LogEntry> CreateLog([FromBody] CreateLogRequest request)
    {
        var logEntry = new LogEntry
        {
            JobId = request.JobId,
            Message = request.Message,
            Level = request.Level,
            Source = request.Source
        };

        var createdLog = _dataStore.AddLog(logEntry);
        _logger.LogDebug("Log entry created for job {JobId}", request.JobId);
        
        return CreatedAtAction(nameof(GetLogs), new { jobId = createdLog.JobId }, createdLog);
    }

    /// <summary>
    /// Get live console logs from the API server
    /// </summary>
    [HttpGet("console")]
    public ActionResult<IEnumerable<ConsoleLogEntry>> GetConsoleLogs([FromQuery] int count = 500, [FromQuery] string? level = null)
    {
        var logs = _consoleLogService.GetLogs(count, level);
        return Ok(logs);
    }

    /// <summary>
    /// Clear console logs
    /// </summary>
    [HttpDelete("console")]
    public ActionResult ClearConsoleLogs()
    {
        _consoleLogService.Clear();
        _logger.LogInformation("Console logs cleared");
        return NoContent();
    }
}
