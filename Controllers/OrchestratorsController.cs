using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IamApi.Models;
using IamApi.Services;

namespace IamApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class OrchestratorsController : ControllerBase
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<OrchestratorsController> _logger;

    public OrchestratorsController(IDataStore dataStore, ILogger<OrchestratorsController> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<IEnumerable<object>> GetOnlineOrchestrators([FromQuery] int minutesThreshold = 1)
    {
        var threshold = DateTime.UtcNow.AddMinutes(-minutesThreshold);
        var allOrchestrators = _dataStore.GetOrchestrators();
        
        var result = allOrchestrators.Select(o => new
        {
            o.Id,
            o.CustomerName,
            o.HostName,
            o.Version,
            o.LastHeartbeat,
            IsOnline = o.LastHeartbeat >= threshold
        })
        .OrderByDescending(o => o.IsOnline)
        .ThenBy(o => o.CustomerName)
        .ThenBy(o => o.HostName);

        return Ok(result);
    }

    [HttpPost("heartbeat")]
    public ActionResult Heartbeat([FromBody] Orchestrator orchestrator)
    {
        if (string.IsNullOrWhiteSpace(orchestrator.Id))
        {
            return BadRequest("Orchestrator ID is required");
        }

        // Preserve PendingUpdate flag from existing record
        var existingOrchestrators = _dataStore.GetOrchestrators();
        var existing = existingOrchestrators.FirstOrDefault(o => o.Id == orchestrator.Id);
        if (existing != null)
        {
            orchestrator.PendingUpdate = existing.PendingUpdate;
        }

        orchestrator.LastHeartbeat = DateTime.UtcNow;
        _dataStore.UpsertOrchestrator(orchestrator);

        _logger.LogDebug("Heartbeat received from orchestrator {Id} for customer {Customer}", 
            orchestrator.Id, orchestrator.CustomerName);

        return Ok();
    }

    [HttpPost("cleanup")]
    public ActionResult CleanupOfflineOrchestrators([FromQuery] int minutesThreshold = 5)
    {
        var threshold = DateTime.UtcNow.AddMinutes(-minutesThreshold);
        var allOrchestrators = _dataStore.GetOrchestrators();
        var offlineOrchestrators = allOrchestrators.Where(o => o.LastHeartbeat < threshold).ToList();
        
        foreach (var orchestrator in offlineOrchestrators)
        {
            _dataStore.RemoveOrchestrator(orchestrator.Id);
        }
        
        _logger.LogInformation("Cleaned up {Count} offline orchestrators (threshold: {Minutes} minutes)", 
            offlineOrchestrators.Count, minutesThreshold);
        
        return Ok(new { removed = offlineOrchestrators.Count, threshold = minutesThreshold });
    }

    [HttpDelete("{id}")]
    public ActionResult DeleteOrchestrator(string id)
    {
        _dataStore.RemoveOrchestrator(id);
        return NoContent();
    }

    /// <summary>
    /// Request an orchestrator to update itself
    /// </summary>
    [HttpPost("{id}/update")]
    public ActionResult RequestUpdate(string id)
    {
        var orchestrators = _dataStore.GetOrchestrators();
        var orchestrator = orchestrators.FirstOrDefault(o => o.Id == id);
        
        if (orchestrator == null)
        {
            return NotFound();
        }

        orchestrator.PendingUpdate = true;
        _dataStore.UpsertOrchestrator(orchestrator);
        
        _logger.LogInformation("Update requested for orchestrator {Id}", id);
        return Ok(new { message = "Update requested", orchestratorId = id });
    }

    /// <summary>
    /// Check if an orchestrator has a pending update (called by orchestrator during heartbeat)
    /// </summary>
    [HttpGet("{id}/update-status")]
    public ActionResult GetUpdateStatus(string id)
    {
        var orchestrators = _dataStore.GetOrchestrators();
        var orchestrator = orchestrators.FirstOrDefault(o => o.Id == id);
        
        if (orchestrator == null)
        {
            return NotFound();
        }

        return Ok(new { pendingUpdate = orchestrator.PendingUpdate });
    }

    /// <summary>
    /// Acknowledge that update is starting (clears the pending flag)
    /// </summary>
    [HttpPost("{id}/update-ack")]
    public ActionResult AcknowledgeUpdate(string id)
    {
        var orchestrators = _dataStore.GetOrchestrators();
        var orchestrator = orchestrators.FirstOrDefault(o => o.Id == id);
        
        if (orchestrator == null)
        {
            return NotFound();
        }

        orchestrator.PendingUpdate = false;
        _dataStore.UpsertOrchestrator(orchestrator);
        
        _logger.LogInformation("Update acknowledged by orchestrator {Id}", id);
        return Ok();
    }
}
