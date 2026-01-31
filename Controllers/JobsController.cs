using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IamApi.Models;
using IamApi.Services;

namespace IamApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IDataStore _dataStore;
    private readonly IContainerRegistryService _registryService;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IDataStore dataStore, 
        IContainerRegistryService registryService,
        ILogger<JobsController> logger)
    {
        _dataStore = dataStore;
        _registryService = registryService;
        _logger = logger;
    }

    /// <summary>
    /// Get all jobs
    /// </summary>
    [HttpGet]
    public ActionResult<IEnumerable<Job>> GetAllJobs()
    {
        var jobs = _dataStore.GetAllJobs();
        return Ok(jobs);
    }

    /// <summary>
    /// Get pending jobs (for orchestrator)
    /// Each orchestrator must specify its ID and handles jobs for a specific customer.
    /// Returns at most ONE job per orchestrator to ensure fair distribution when multiple orchestrators handle the same customer.
    /// </summary>
    [HttpGet("pending")]
    public ActionResult<IEnumerable<Job>> GetPendingJobs(
        [FromQuery] string? orchestratorId = null,
        [FromQuery] string? customer = null)
    {
        // Orchestrator ID is required to ensure proper job assignment
        if (string.IsNullOrEmpty(orchestratorId))
        {
            return BadRequest("orchestratorId is required. Each orchestrator must identify itself.");
        }
        
        // Customer is required - no "all customers" orchestrators allowed
        if (string.IsNullOrEmpty(customer))
        {
            return BadRequest("customer is required. Each orchestrator must handle a specific customer.");
        }
        
        // Verify orchestrator is registered and handles this customer
        var orchestrators = _dataStore.GetOrchestrators();
        var orchestrator = orchestrators.FirstOrDefault(o => o.Id == orchestratorId);
        
        if (orchestrator == null)
        {
            return BadRequest($"Orchestrator '{orchestratorId}' is not registered. Send a heartbeat first.");
        }
        
        if (!orchestrator.CustomerName.Equals(customer, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest($"Orchestrator '{orchestratorId}' is registered for customer '{orchestrator.CustomerName}', not '{customer}'.");
        }
        
        var jobs = _dataStore.GetPendingJobs()
            .Where(j => j.Customer.Equals(customer, StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        // Check if this orchestrator already has a job assigned (running)
        var runningJobs = _dataStore.GetAllJobs()
            .Where(j => j.Status == JobStatus.Running && 
                        j.AssignedToOrchestratorId == orchestratorId)
            .ToList();
        
        if (runningJobs.Any())
        {
            // Orchestrator already has a running job - don't give another one
            _logger.LogDebug("Orchestrator {OrchestratorId} already has {Count} running job(s), not assigning new job",
                orchestratorId, runningJobs.Count);
            return Ok(Enumerable.Empty<Job>());
        }
        
        // Find a job that is not assigned to any orchestrator, or was assigned to this one
        var availableJob = jobs
            .Where(j => string.IsNullOrEmpty(j.AssignedToOrchestratorId) || 
                        j.AssignedToOrchestratorId == orchestratorId)
            .OrderBy(j => j.CreatedAt) // FIFO - oldest first
            .FirstOrDefault();
        
        if (availableJob != null)
        {
            // Assign the job to this orchestrator
            availableJob.AssignedToOrchestratorId = orchestratorId;
            _dataStore.UpdateJob(availableJob);
            
            _logger.LogInformation("Assigned job {JobId} ({JobName}) to orchestrator {OrchestratorId} for customer {Customer}",
                availableJob.Id, availableJob.Name, orchestratorId, customer);
            
            return Ok(new[] { availableJob });
        }
        
        return Ok(Enumerable.Empty<Job>());
    }

    /// <summary>
    /// Get a specific job
    /// </summary>
    [HttpGet("{id}")]
    public ActionResult<Job> GetJob(Guid id)
    {
        var job = _dataStore.GetJob(id);
        if (job == null)
        {
            return NotFound();
        }
        return Ok(job);
    }

    /// <summary>
    /// Create a new job
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Job>> CreateJob([FromBody] CreateJobRequest request)
    {
        // Debug logging for registry credentials
        _logger.LogDebug("CreateJob - Registry Server: {Server}, Username: {Username}, Password present: {PasswordPresent}, RegistryId: {RegistryId}",
            request.RegistryServer ?? "null",
            request.RegistryUsername ?? "null",
            !string.IsNullOrEmpty(request.RegistryPassword),
            request.RegistryId);
        
        var job = new Job
        {
            Name = request.Name,
            Customer = request.Customer,
            ScriptPath = request.ScriptPath,
            ContainerImage = request.ContainerImage,
            RegistryServer = request.RegistryServer,
            RegistryUsername = request.RegistryUsername,
            RegistryPassword = request.RegistryPassword,
            RegistryId = request.RegistryId,
            JobType = request.JobType,
            IsWhatIf = request.IsWhatIf,
            Schedule = request.Schedule,
            ScheduledFor = request.ScheduledFor,
            Parameters = request.Parameters
        };

        // If an ACR registry and image repository is specified, create scope map and token
        if (request.RegistryId.HasValue && !string.IsNullOrEmpty(request.ImageRepository))
        {
            var registry = _dataStore.GetRegistry(request.RegistryId.Value);
            if (registry != null && registry.Type == RegistryType.AzureContainerRegistry)
            {
                try
                {
                    _logger.LogInformation("Creating ACR scope map and token for job {JobName} with repository {Repository}",
                        request.Name, request.ImageRepository);

                    // Create a unique scope map name for this job
                    var scopeMapName = $"job-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    
                    var scopeMapRequest = new CreateScopeMapRequest
                    {
                        Name = scopeMapName,
                        Description = $"Auto-created for job: {request.Name}",
                        Repositories = new List<string> { request.ImageRepository },
                        Actions = new List<string> { "content/read", "metadata/read" }
                    };
                    
                    var scopeMap = await _registryService.CreateScopeMapAsync(request.RegistryId.Value, scopeMapRequest);
                    job.AcrScopeMapId = scopeMap.Id;
                    _logger.LogInformation("Created scope map {ScopeMapName} for job", scopeMapName);

                    // Create token for the scope map
                    var tokenName = $"job-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    var tokenRequest = new CreateTokenRequest
                    {
                        Name = tokenName,
                        ScopeMapId = scopeMap.Id,
                        AssignToJobId = job.Id
                    };
                    
                    var tokenResponse = await _registryService.CreateTokenAsync(request.RegistryId.Value, tokenRequest);
                    job.AcrTokenId = tokenResponse.Id;
                    
                    // Set the registry credentials from the newly created token
                    job.RegistryServer = registry.Server;
                    job.RegistryUsername = tokenResponse.Username;
                    job.RegistryPassword = tokenResponse.Password;
                    
                    _logger.LogInformation("Created token {TokenName} for job with username {Username}",
                        tokenName, tokenResponse.Username);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create ACR scope map/token for job {JobName}", request.Name);
                    return BadRequest(new { error = $"Failed to create ACR credentials: {ex.Message}" });
                }
            }
        }

        var createdJob = _dataStore.AddJob(job);
        _logger.LogInformation("Created job {JobId} ({JobName}) for customer {Customer}",
            createdJob.Id, createdJob.Name, createdJob.Customer);
        
        return CreatedAtAction(nameof(GetJob), new { id = createdJob.Id }, createdJob);
    }

    /// <summary>
    /// Update job status (for orchestrator)
    /// </summary>
    [HttpPatch("{id}/status")]
    public async Task<ActionResult<Job>> UpdateJobStatus(Guid id, [FromBody] JobStatus status)
    {
        // Get the job first to check for ACR resources before updating status
        var existingJob = _dataStore.GetJob(id);
        if (existingJob == null)
        {
            return NotFound();
        }
        
        var job = _dataStore.UpdateJobStatus(id, status);
        if (job == null)
        {
            return NotFound();
        }

        _logger.LogInformation("Updated job {JobId} status to {Status}", id, status);

        // Clean up orchestrator assignment and ACR resources when job completes or fails
        if (status == JobStatus.Completed || status == JobStatus.Failed)
        {
            // Clear the orchestrator assignment so the job slot becomes available
            if (!string.IsNullOrEmpty(job.AssignedToOrchestratorId))
            {
                _logger.LogInformation("Clearing orchestrator assignment for completed/failed job {JobId}", id);
                job.AssignedToOrchestratorId = null;
                _dataStore.UpdateJob(job);
            }
            
            await CleanupAcrResourcesAsync(job);
        }

        return Ok(job);
    }

    /// <summary>
    /// Clean up ACR scope map and token for a job
    /// </summary>
    private async Task CleanupAcrResourcesAsync(Job job)
    {
        if (job.AcrTokenId.HasValue)
        {
            try
            {
                _logger.LogInformation("Cleaning up ACR token {TokenId} for completed job {JobId}", job.AcrTokenId, job.Id);
                await _registryService.DeleteTokenAsync(job.AcrTokenId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup ACR token {TokenId} for job {JobId}", job.AcrTokenId, job.Id);
            }
        }

        if (job.AcrScopeMapId.HasValue)
        {
            try
            {
                _logger.LogInformation("Cleaning up ACR scope map {ScopeMapId} for completed job {JobId}", job.AcrScopeMapId, job.Id);
                await _registryService.DeleteScopeMapAsync(job.AcrScopeMapId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup ACR scope map {ScopeMapId} for job {JobId}", job.AcrScopeMapId, job.Id);
            }
        }
    }

    /// <summary>
    /// Toggle pause state for a scheduled job
    /// </summary>
    [HttpPatch("{id}/pause")]
    public ActionResult<Job> TogglePause(Guid id, [FromBody] bool isPaused)
    {
        var job = _dataStore.GetJob(id);
        if (job == null)
        {
            return NotFound();
        }

        if (job.JobType != JobType.Scheduled)
        {
            return BadRequest("Only scheduled jobs can be paused");
        }

        job.IsPaused = isPaused;
        var updatedJob = _dataStore.UpdateJob(job);
        if (updatedJob == null)
        {
            return NotFound();
        }
        
        _logger.LogInformation("Job {JobId} pause state set to {IsPaused}", id, isPaused);
        return Ok(updatedJob);
    }

    /// <summary>
    /// Update a job
    /// </summary>
    [HttpPut("{id}")]
    public ActionResult<Job> UpdateJob(Guid id, [FromBody] CreateJobRequest request)
    {
        var existingJob = _dataStore.GetJob(id);
        if (existingJob == null)
        {
            return NotFound();
        }

        existingJob.Name = request.Name;
        existingJob.Customer = request.Customer;
        existingJob.ContainerImage = request.ContainerImage;
        existingJob.ScriptPath = request.ScriptPath;
        existingJob.JobType = request.JobType;
        existingJob.IsWhatIf = request.IsWhatIf;
        existingJob.Schedule = request.Schedule;
        existingJob.Parameters = request.Parameters;

        var updatedJob = _dataStore.UpdateJob(existingJob);
        if (updatedJob == null)
        {
            return NotFound();
        }

        _logger.LogInformation("Updated job {JobId}", id);
        return Ok(updatedJob);
    }

    /// <summary>
    /// Delete a job
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteJob(Guid id)
    {
        var job = _dataStore.GetJob(id);
        if (job == null)
        {
            return NotFound();
        }

        // Clean up ACR token and scope map if they exist
        await CleanupAcrResourcesAsync(job);

        var deleted = _dataStore.DeleteJob(id);
        if (!deleted)
        {
            return NotFound();
        }

        _logger.LogInformation("Deleted job {JobId}", id);
        return NoContent();
    }

    /// <summary>
    /// Retry a failed job (reset to pending)
    /// </summary>
    [HttpPost("{id}/retry")]
    public ActionResult<Job> RetryJob(Guid id)
    {
        var job = _dataStore.GetJob(id);
        if (job == null)
        {
            return NotFound();
        }

        if (job.Status != JobStatus.Failed)
        {
            return BadRequest("Only failed jobs can be retried");
        }

        var updatedJob = _dataStore.UpdateJobStatus(id, JobStatus.Pending);
        if (updatedJob == null)
        {
            return NotFound();
        }

        // Clear the completion timestamp and orchestrator assignment to allow re-execution
        updatedJob.CompletedAt = null;
        updatedJob.StartedAt = null;
        updatedJob.AssignedToOrchestratorId = null;
        _dataStore.UpdateJob(updatedJob);

        _logger.LogInformation("Job {JobId} reset to pending for retry", id);
        return Ok(updatedJob);
    }
}
