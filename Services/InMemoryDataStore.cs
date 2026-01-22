using System.Collections.Concurrent;
using IamApi.Models;

namespace IamApi.Services;

public class InMemoryDataStore : IDataStore
{
    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly ConcurrentBag<LogEntry> _logs = new();
    private readonly ConcurrentDictionary<string, Orchestrator> _orchestrators = new();

    // Jobs
    public Job AddJob(Job job)
    {
        job.Id = Guid.NewGuid();
        job.CreatedAt = DateTime.UtcNow;
        job.Status = JobStatus.Pending;
        _jobs[job.Id] = job;
        return job;
    }

    public Job? GetJob(Guid id)
    {
        _jobs.TryGetValue(id, out var job);
        return job;
    }

    public IEnumerable<Job> GetAllJobs()
    {
        return _jobs.Values.OrderByDescending(j => j.CreatedAt);
    }

    public IEnumerable<Job> GetPendingJobs()
    {
        return _jobs.Values
            .Where(j => j.Status == JobStatus.Pending)
            .OrderBy(j => j.CreatedAt);
    }

    public Job? UpdateJobStatus(Guid id, JobStatus status)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            job.Status = status;
            
            if (status == JobStatus.Running && !job.StartedAt.HasValue)
            {
                job.StartedAt = DateTime.UtcNow;
            }
            else if ((status == JobStatus.Completed || status == JobStatus.Failed) && !job.CompletedAt.HasValue)
            {
                job.CompletedAt = DateTime.UtcNow;
            }
            
            return job;
        }
        return null;
    }

    public Job? UpdateJob(Job job)
    {
        if (_jobs.TryGetValue(job.Id, out var existingJob))
        {
            existingJob.Name = job.Name;
            existingJob.Customer = job.Customer;
            existingJob.ContainerImage = job.ContainerImage;
            existingJob.ScriptPath = job.ScriptPath;
            existingJob.RegistryServer = job.RegistryServer;
            existingJob.RegistryUsername = job.RegistryUsername;
            existingJob.RegistryPassword = job.RegistryPassword;
            existingJob.JobType = job.JobType;
            existingJob.IsWhatIf = job.IsWhatIf;
            existingJob.IsPaused = job.IsPaused;
            existingJob.Schedule = job.Schedule;
            existingJob.Parameters = job.Parameters;
            return existingJob;
        }
        return null;
    }

    public bool DeleteJob(Guid id)
    {
        return _jobs.TryRemove(id, out _);
    }

    // Logs
    public LogEntry AddLog(LogEntry log)
    {
        log.Id = Guid.NewGuid();
        log.Timestamp = DateTime.UtcNow;
        _logs.Add(log);
        return log;
    }

    public IEnumerable<LogEntry> GetLogs(Guid? jobId = null)
    {
        var logs = _logs.AsEnumerable();
        if (jobId.HasValue)
        {
            logs = logs.Where(l => l.JobId == jobId.Value);
        }
        return logs.OrderByDescending(l => l.Timestamp);
    }

    public IEnumerable<LogEntry> GetLogsByJob(Guid jobId)
    {
        return _logs.Where(l => l.JobId == jobId).OrderBy(l => l.Timestamp);
    }
    
    // Orchestrators
    public IEnumerable<Orchestrator> GetOrchestrators()
    {
        return _orchestrators.Values;
    }

    public void UpsertOrchestrator(Orchestrator orchestrator)
    {
        _orchestrators.AddOrUpdate(orchestrator.Id, orchestrator, (key, existing) => orchestrator);
    }

    public void RemoveOrchestrator(string id)
    {
        _orchestrators.TryRemove(id, out _);
    }
}
