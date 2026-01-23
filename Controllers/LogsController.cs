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

    public LogsController(IDataStore dataStore, ILogger<LogsController> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    /// <summary>
    /// Get all logs, optionally filtered by job ID
    /// </summary>
    [HttpGet]
    public ActionResult<IEnumerable<LogEntry>> GetLogs([FromQuery] Guid? jobId = null)
    {
        var logs = _dataStore.GetLogs(jobId);
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
}
