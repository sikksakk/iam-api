using Microsoft.AspNetCore.Mvc;
using IamApi.Models;
using IamApi.Services;

namespace IamApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<JobsController> _logger;

    public JobsController(IDataStore dataStore, ILogger<JobsController> logger)
    {
        _dataStore = dataStore;
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
    /// </summary>
    [HttpGet("pending")]
    public ActionResult<IEnumerable<Job>> GetPendingJobs([FromQuery] string? customer = null)
    {
        var jobs = _dataStore.GetPendingJobs();
        
        if (!string.IsNullOrEmpty(customer))
        {
            jobs = jobs.Where(j => j.Customer.Equals(customer, StringComparison.OrdinalIgnoreCase));
        }
        
        return Ok(jobs);
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
    public ActionResult<Job> CreateJob([FromBody] CreateJobRequest request)
    {
        // Debug logging for registry credentials
        _logger.LogDebug("CreateJob - Registry Server: {Server}, Username: {Username}, Password present: {PasswordPresent}, Password length: {PasswordLength}",
            request.RegistryServer ?? "null",
            request.RegistryUsername ?? "null",
            !string.IsNullOrEmpty(request.RegistryPassword),
            request.RegistryPassword?.Length ?? 0);
        
        var job = new Job
        {
            Name = request.Name,
            Customer = request.Customer,
            ScriptPath = request.ScriptPath,
            ContainerImage = request.ContainerImage,
            RegistryServer = request.RegistryServer,
            RegistryUsername = request.RegistryUsername,
            RegistryPassword = request.RegistryPassword,
            JobType = request.JobType,
            IsWhatIf = request.IsWhatIf,
            Schedule = request.Schedule,
            Parameters = request.Parameters
        };

        var createdJob = _dataStore.AddJob(job);
        _logger.LogInformation("Created job {JobId} ({JobName}) for customer {Customer}",
            createdJob.Id, createdJob.Name, createdJob.Customer);
        _logger.LogDebug("Job {JobId} - Stored password present: {PasswordPresent}, length: {Length}",
            createdJob.Id,
            !string.IsNullOrEmpty(createdJob.RegistryPassword),
            createdJob.RegistryPassword?.Length ?? 0);
        
        return CreatedAtAction(nameof(GetJob), new { id = createdJob.Id }, createdJob);
    }

    /// <summary>
    /// Update job status (for orchestrator)
    /// </summary>
    [HttpPatch("{id}/status")]
    public ActionResult<Job> UpdateJobStatus(Guid id, [FromBody] JobStatus status)
    {
        var job = _dataStore.UpdateJobStatus(id, status);
        if (job == null)
        {
            return NotFound();
        }

        _logger.LogInformation("Updated job {JobId} status to {Status}", id, status);
        return Ok(job);
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
        _logger.LogInformation("Job {JobId} pause state set to {IsPaused}", id, isPaused);
        return Ok(job);
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
    public ActionResult DeleteJob(Guid id)
    {
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

        // Clear the completion timestamp to allow re-execution
        updatedJob.CompletedAt = null;
        updatedJob.StartedAt = null;

        _logger.LogInformation("Job {JobId} reset to pending for retry", id);
        return Ok(updatedJob);
    }
}
