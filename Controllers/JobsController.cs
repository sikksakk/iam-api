using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IamApi.Models;
using IamApi.Services;

namespace IamApi.Controllers;

[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class JobsController : ControllerBase
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
        
        // OPTIMIZATION: Single query instead of N+1 - get all jobs once and filter in memory
        var allJobs = _dataStore.GetAllJobs().ToList();
        
        // Check if this orchestrator already has a job assigned (running)
        var hasRunningJob = allJobs.Any(j => 
            j.Status == JobStatus.Running && 
            j.AssignedToOrchestratorId == orchestratorId);
        
        if (hasRunningJob)
        {
            // Orchestrator already has a running job - don't give another one
            _logger.LogDebug("Orchestrator {OrchestratorId} already has running job(s), not assigning new job", orchestratorId);
            return Ok(Enumerable.Empty<Job>());
        }
        
        // Find pending jobs for this customer
        var availableJob = allJobs
            .Where(j => j.Status == JobStatus.Pending &&
                        j.Customer.Equals(customer, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(j.AssignedToOrchestratorId) || j.AssignedToOrchestratorId == orchestratorId))
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

        // Regenerate ACR credentials when transitioning to Running if needed
        if (status == JobStatus.Running && job.RegistryId.HasValue)
        {
            _logger.LogDebug("Job {JobId} transitioning to Running - checking ACR credentials. RegistryId: {RegistryId}, RegistryUsername: {Username}, HasPassword: {HasPassword}, AcrTokenId: {TokenId}",
                id, 
                job.RegistryId?.ToString() ?? "[NOT SET]",
                job.RegistryUsername ?? "[NOT SET]",
                !string.IsNullOrEmpty(job.RegistryPassword) ? "YES" : "NO",
                job.AcrTokenId?.ToString() ?? "[NOT SET]");
            
            // Check if we need new credentials:
            // 1. No credentials at all
            // 2. Has AcrTokenId but token no longer exists in our data store (was deleted)
            var needsNewCredentials = string.IsNullOrEmpty(job.RegistryUsername) || 
                                       string.IsNullOrEmpty(job.RegistryPassword) ||
                                       !job.AcrTokenId.HasValue;
            
            // If we have an AcrTokenId, verify the token still exists
            if (job.AcrTokenId.HasValue && !needsNewCredentials)
            {
                var existingToken = _dataStore.GetToken(job.AcrTokenId.Value);
                if (existingToken == null)
                {
                    _logger.LogWarning("Job {JobId} references AcrTokenId {TokenId} but token no longer exists - will regenerate", 
                        id, job.AcrTokenId.Value);
                    needsNewCredentials = true;
                }
                else
                {
                    _logger.LogDebug("Job {JobId} has valid existing token {TokenId} ({Username})", 
                        id, job.AcrTokenId.Value, existingToken.Username);
                }
            }
            
            if (needsNewCredentials)
            {
                _logger.LogInformation("Job {JobId} needs fresh ACR credentials - regenerating (old Username={OldUsername}, AcrTokenId={OldTokenId})", 
                    id, job.RegistryUsername ?? "[NONE]", job.AcrTokenId?.ToString() ?? "[NONE]");
                
                // Clear stale credentials
                job.RegistryUsername = null;
                job.RegistryPassword = null;
                job.AcrTokenId = null;
                job.AcrScopeMapId = null;
                
                await RegenerateAcrCredentialsIfNeededAsync(job);
                
                _logger.LogInformation("ACR credentials generated for job {JobId}: Username={Username}, HasPassword={HasPassword}, AcrTokenId={TokenId}",
                    id,
                    job.RegistryUsername ?? "[NOT SET]",
                    !string.IsNullOrEmpty(job.RegistryPassword) ? "YES" : "NO",
                    job.AcrTokenId?.ToString() ?? "[NOT SET]");
            }
            else
            {
                _logger.LogDebug("Job {JobId} already has valid ACR credentials - no regeneration needed", id);
            }
        }
        else if (status == JobStatus.Running)
        {
            _logger.LogDebug("Job {JobId} has no RegistryId - ACR credential check not needed", id);
        }

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
    /// Regenerate ACR credentials for a scheduled job run
    /// </summary>
    private async Task RegenerateAcrCredentialsIfNeededAsync(Job job)
    {
        _logger.LogDebug("RegenerateAcrCredentialsIfNeededAsync: JobId={JobId}, RegistryId={RegistryId}",
            job.Id, job.RegistryId);
            
        if (!job.RegistryId.HasValue)
        {
            _logger.LogDebug("Job {JobId} has no RegistryId - skipping ACR credential regeneration", job.Id);
            return;
        }
            
        var registry = _dataStore.GetRegistry(job.RegistryId.Value);
        if (registry == null)
        {
            _logger.LogWarning("Registry {RegistryId} not found for job {JobId}", job.RegistryId.Value, job.Id);
            return;
        }
        
        if (registry.Type != RegistryType.AzureContainerRegistry)
        {
            _logger.LogDebug("Registry {RegistryId} is not ACR (Type={Type}) - skipping credential regeneration",
                job.RegistryId.Value, registry.Type);
            return;
        }
            
        try
        {
            // Extract repository from container image
            var imageRepository = ExtractRepositoryFromImage(job.ContainerImage, registry.Server);
            
            if (string.IsNullOrEmpty(imageRepository))
            {
                _logger.LogWarning("Could not extract repository from container image {Image} for scheduled job {JobId}",
                    job.ContainerImage, job.Id);
                return;
            }
            
            _logger.LogInformation("Creating ACR scope map and token for job {JobId} ({JobName}) with repository {Repository}",
                job.Id, job.Name, imageRepository);
            _logger.LogDebug("ACR credential regeneration - Registry: {RegistryServer}, RegistryId: {RegistryId}",
                registry.Server, job.RegistryId);

            // Create a unique scope map name for this run
            var scopeMapName = $"job-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            
            var scopeMapRequest = new CreateScopeMapRequest
            {
                Name = scopeMapName,
                Description = $"Auto-created for scheduled run of job: {job.Name}",
                Repositories = new List<string> { imageRepository },
                Actions = new List<string> { "content/read", "metadata/read" }
            };
            
            var scopeMap = await _registryService.CreateScopeMapAsync(job.RegistryId.Value, scopeMapRequest);
            job.AcrScopeMapId = scopeMap.Id;
            _logger.LogInformation("Created ACR scope map {ScopeMapName} (ID: {ScopeMapId}) for job {JobId} - Repositories: [{Repositories}], Actions: [{Actions}]",
                scopeMapName, scopeMap.Id, job.Id, string.Join(", ", scopeMapRequest.Repositories), string.Join(", ", scopeMapRequest.Actions));

            // Create token for the scope map
            var tokenName = $"job-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            var tokenRequest = new CreateTokenRequest
            {
                Name = tokenName,
                ScopeMapId = scopeMap.Id,
                AssignToJobId = job.Id
            };
            
            var tokenResponse = await _registryService.CreateTokenAsync(job.RegistryId.Value, tokenRequest);
            job.AcrTokenId = tokenResponse.Id;
            
            // Set the registry credentials from the newly created token
            job.RegistryServer = registry.Server;
            job.RegistryUsername = tokenResponse.Username;
            job.RegistryPassword = tokenResponse.Password;
            
            _dataStore.UpdateJob(job);
            
            _logger.LogInformation("Created ACR token {TokenName} (ID: {TokenId}) for job {JobId} - Username: {Username}, RegistryServer: {RegistryServer}",
                tokenName, tokenResponse.Id, job.Id, tokenResponse.Username, job.RegistryServer);
            _logger.LogInformation("ACR credentials successfully regenerated for job {JobId} ({JobName})", job.Id, job.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ACR scope map/token for scheduled job run {JobId}. Job will attempt to use existing credentials.", job.Id);
            // Don't fail the job - it might still work with static credentials
        }
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
        
        // Clear old ACR credentials (they were cleaned up from Azure when the job failed)
        // This forces regeneration when the job transitions to Running
        if (updatedJob.RegistryId.HasValue)
        {
            _logger.LogInformation("Clearing old ACR credentials for retried job {JobId}", id);
            updatedJob.RegistryUsername = null;
            updatedJob.RegistryPassword = null;
        }
        
        // Clear old ACR token/scope map references (they were cleaned up when the job failed)
        updatedJob.AcrTokenId = null;
        updatedJob.AcrScopeMapId = null;
        
        // ACR credentials will be regenerated when the orchestrator picks up the job
        // and updates the status to Running (handled in UpdateJobStatus)
        
        _dataStore.UpdateJob(updatedJob);

        _logger.LogInformation("Job {JobId} reset to pending for retry", id);
        return Ok(updatedJob);
    }
    
    /// <summary>
    /// Extracts the repository name from a container image reference
    /// </summary>
    private static string? ExtractRepositoryFromImage(string containerImage, string registryServer)
    {
        if (string.IsNullOrEmpty(containerImage))
            return null;
            
        // Remove registry server prefix if present
        var image = containerImage;
        if (image.StartsWith(registryServer, StringComparison.OrdinalIgnoreCase))
        {
            image = image.Substring(registryServer.Length).TrimStart('/');
        }
        
        // Remove tag if present (e.g., "repo:tag" -> "repo")
        var colonIndex = image.IndexOf(':');
        if (colonIndex > 0)
        {
            image = image.Substring(0, colonIndex);
        }
        
        // Remove digest if present (e.g., "repo@sha256:..." -> "repo")
        var atIndex = image.IndexOf('@');
        if (atIndex > 0)
        {
            image = image.Substring(0, atIndex);
        }
        
        return string.IsNullOrEmpty(image) ? null : image;
    }

    /// <summary>
    /// Refresh ACR credentials for a job (called by orchestrator when auth fails)
    /// </summary>
    [HttpPost("{id}/refresh-credentials")]
    public async Task<ActionResult<Job>> RefreshCredentials(Guid id)
    {
        var job = _dataStore.GetJob(id);
        if (job == null)
        {
            return NotFound();
        }

        // Only allow refresh for pending or running jobs
        if (job.Status != JobStatus.Pending && job.Status != JobStatus.Running)
        {
            return BadRequest($"Cannot refresh credentials for job with status {job.Status}");
        }

        // Must have a registry ID to regenerate credentials
        if (!job.RegistryId.HasValue)
        {
            return BadRequest("Job does not have a registry ID. Cannot regenerate ACR credentials.");
        }

        var registry = _dataStore.GetRegistry(job.RegistryId.Value);
        if (registry == null)
        {
            return BadRequest($"Registry with ID {job.RegistryId} not found");
        }

        if (registry.Type != RegistryType.AzureContainerRegistry)
        {
            return BadRequest("Credential refresh is only supported for Azure Container Registry");
        }

        try
        {
            _logger.LogInformation("Refreshing ACR credentials for job {JobId} ({JobName})", id, job.Name);

            // Clean up old token and scope map if they exist
            await CleanupAcrResourcesAsync(job);

            // Extract repository from container image
            // Format: registry.azurecr.io/repo/path:tag -> repo/path
            var imageRepository = ExtractRepositoryFromImage(job.ContainerImage, registry.Server);
            if (string.IsNullOrEmpty(imageRepository))
            {
                return BadRequest($"Could not extract repository from container image: {job.ContainerImage}");
            }

            // Create a new scope map
            var scopeMapName = $"job-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            var scopeMapRequest = new CreateScopeMapRequest
            {
                Name = scopeMapName,
                Description = $"Auto-created for job: {job.Name} (refreshed)",
                Repositories = new List<string> { imageRepository },
                Actions = new List<string> { "content/read", "metadata/read" }
            };
            
            var scopeMap = await _registryService.CreateScopeMapAsync(job.RegistryId.Value, scopeMapRequest);
            job.AcrScopeMapId = scopeMap.Id;
            _logger.LogInformation("Created new scope map {ScopeMapName} for job {JobId}", scopeMapName, id);

            // Create new token for the scope map
            var tokenName = $"job-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            var tokenRequest = new CreateTokenRequest
            {
                Name = tokenName,
                ScopeMapId = scopeMap.Id,
                AssignToJobId = job.Id
            };
            
            var tokenResponse = await _registryService.CreateTokenAsync(job.RegistryId.Value, tokenRequest);
            job.AcrTokenId = tokenResponse.Id;
            
            // Update the registry credentials
            job.RegistryServer = registry.Server;
            job.RegistryUsername = tokenResponse.Username;
            job.RegistryPassword = tokenResponse.Password;
            
            _dataStore.UpdateJob(job);
            
            _logger.LogInformation("Refreshed ACR credentials for job {JobId}. New token: {TokenName}, username: {Username}",
                id, tokenName, tokenResponse.Username);

            return Ok(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh ACR credentials for job {JobId}", id);
            return StatusCode(500, new { error = $"Failed to refresh ACR credentials: {ex.Message}" });
        }
    }
}
