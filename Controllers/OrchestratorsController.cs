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
}
