using IamApi.Models;
using IamApi.Services;
using NCrontab;

namespace IamApi;

/// <summary>
/// Background worker that monitors scheduled jobs and creates pending job instances
/// when they are due to run. This centralizes scheduling logic in the API so that
/// orchestrators can treat all jobs as simple one-off executions.
/// </summary>
public class JobSchedulingWorker : BackgroundService
{
    private readonly ILogger<JobSchedulingWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);
    
    // Track last run time for each scheduled job to prevent duplicate triggers
    private readonly Dictionary<Guid, DateTime> _lastRunTimes = new();

    public JobSchedulingWorker(
        ILogger<JobSchedulingWorker> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job Scheduling Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckScheduledJobsAsync();
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in job scheduling worker");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Job Scheduling Worker stopped");
    }

    private async Task CheckScheduledJobsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dataStore = scope.ServiceProvider.GetRequiredService<IDataStore>();

        var allJobs = dataStore.GetAllJobs();
        // Only check jobs that are in Scheduled status (waiting for their time)
        var scheduledJobs = allJobs.Where(j => 
            j.JobType == JobType.Scheduled && 
            j.Status == JobStatus.Scheduled &&
            !string.IsNullOrEmpty(j.Schedule) &&
            !j.IsPaused).ToList();

        foreach (var job in scheduledJobs)
        {
            try
            {
                await CheckAndTriggerJobAsync(job, dataStore);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking scheduled job {JobId}", job.Id);
            }
        }
    }

    private async Task CheckAndTriggerJobAsync(Job job, IDataStore dataStore)
    {
        if (string.IsNullOrEmpty(job.Schedule))
            return;

        try
        {
            var schedule = CrontabSchedule.Parse(job.Schedule);
            var now = DateTime.UtcNow;
            
            // Get the last occurrence that should have happened
            var lastOccurrence = GetLastOccurrence(schedule, now);
            
            // Check if we've already triggered for this occurrence
            if (_lastRunTimes.TryGetValue(job.Id, out var lastRun))
            {
                // If we already ran within the current minute window, skip
                if (lastRun >= lastOccurrence)
                {
                    return;
                }
            }

            // Check if the job is currently running - don't trigger again
            if (job.Status == JobStatus.Running)
            {
                _logger.LogDebug("Job {JobId} is currently running, skipping trigger", job.Id);
                return;
            }

            // Check if this occurrence is within the check window (last 2 minutes)
            var windowStart = now.AddMinutes(-2);
            if (lastOccurrence >= windowStart && lastOccurrence <= now)
            {
                // Time to trigger this job!
                _logger.LogInformation("Triggering scheduled job {JobId} ({JobName}) - Schedule: {Schedule}", 
                    job.Id, job.Name, job.Schedule);

                // Reset the job to Pending status so an orchestrator picks it up
                job.Status = JobStatus.Pending;
                job.StartedAt = null;
                job.CompletedAt = null;
                dataStore.UpdateJob(job);

                // Record that we triggered this occurrence
                _lastRunTimes[job.Id] = lastOccurrence;

                _logger.LogInformation("Job {JobId} set to Pending for scheduled execution", job.Id);
            }
        }
        catch (CrontabException ex)
        {
            _logger.LogWarning("Invalid cron expression for job {JobId}: {Schedule} - {Error}", 
                job.Id, job.Schedule, ex.Message);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets the most recent occurrence time that is at or before the current time.
    /// </summary>
    private DateTime GetLastOccurrence(CrontabSchedule schedule, DateTime now)
    {
        // NCrontab only provides GetNextOccurrence, so we work backwards
        // by checking if the next occurrence from (now - 1 minute) is <= now
        var checkTime = now.AddMinutes(-1);
        var nextFromCheck = schedule.GetNextOccurrence(checkTime);
        
        // If the next occurrence from a minute ago is now or in the past, that's our target
        if (nextFromCheck <= now)
        {
            return nextFromCheck;
        }
        
        // Otherwise, go back further
        checkTime = now.AddMinutes(-2);
        nextFromCheck = schedule.GetNextOccurrence(checkTime);
        
        if (nextFromCheck <= now)
        {
            return nextFromCheck;
        }

        // No recent occurrence
        return DateTime.MinValue;
    }
}
