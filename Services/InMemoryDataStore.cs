using System.Collections.Concurrent;
using IamApi.Models;

namespace IamApi.Services;

public sealed class InMemoryDataStore : IDataStore
{
    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();
    private readonly ConcurrentQueue<LogEntry> _logs = new();
    private readonly ConcurrentDictionary<string, Orchestrator> _orchestrators = new();
    
    // Maximum number of log entries to keep in memory
    private const int MaxLogEntries = 10000;
    private readonly ConcurrentDictionary<Guid, ContainerRegistry> _registries = new();
    private readonly ConcurrentDictionary<string, Customer> _customers = new();
    private readonly ConcurrentDictionary<Guid, AcrScopeMap> _scopeMaps = new();
    private readonly ConcurrentDictionary<Guid, AcrToken> _tokens = new();

    // Jobs
    public Job AddJob(Job job)
    {
        job.Id = Guid.NewGuid();
        job.CreatedAt = DateTime.UtcNow;
        // Scheduled jobs wait for their schedule, one-off jobs are immediately pending
        job.Status = job.JobType == JobType.Scheduled ? JobStatus.Scheduled : JobStatus.Pending;
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
            else if (status == JobStatus.Completed || status == JobStatus.Failed)
            {
                job.CompletedAt = DateTime.UtcNow;
                
                // Scheduled jobs should reset to Scheduled status after completion
                // so they can be triggered again by the scheduler
                if (job.JobType == JobType.Scheduled && !string.IsNullOrEmpty(job.Schedule))
                {
                    job.Status = JobStatus.Scheduled;
                    job.StartedAt = null;
                    job.CompletedAt = null;
                }
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
        _logs.Enqueue(log);
        
        // Prune old entries to prevent unbounded memory growth
        while (_logs.Count > MaxLogEntries && _logs.TryDequeue(out _)) { }
        
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
    
    // Container Registries
    public IEnumerable<ContainerRegistry> GetRegistries()
    {
        return _registries.Values.OrderBy(r => r.Name);
    }

    public ContainerRegistry? GetRegistry(Guid id)
    {
        _registries.TryGetValue(id, out var registry);
        return registry;
    }

    public void AddRegistry(ContainerRegistry registry)
    {
        _registries[registry.Id] = registry;
    }

    public void RemoveRegistry(Guid id)
    {
        _registries.TryRemove(id, out _);
    }

    // Customers
    public IEnumerable<Customer> GetCustomers()
    {
        return _customers.Values.OrderBy(c => c.Name);
    }

    public Customer? GetCustomer(string id)
    {
        _customers.TryGetValue(id, out var customer);
        return customer;
    }

    public void AddCustomer(Customer customer)
    {
        _customers[customer.Id] = customer;
    }

    public void UpdateCustomer(Customer customer)
    {
        _customers[customer.Id] = customer;
    }

    public void DeleteCustomer(string id)
    {
        _customers.TryRemove(id, out _);
    }

    // ACR Scope Maps
    public void AddScopeMap(AcrScopeMap scopeMap)
    {
        _scopeMaps[scopeMap.Id] = scopeMap;
    }

    public AcrScopeMap? GetScopeMap(Guid id)
    {
        _scopeMaps.TryGetValue(id, out var scopeMap);
        return scopeMap;
    }

    public List<AcrScopeMap> GetScopeMaps(Guid registryId)
    {
        return _scopeMaps.Values
            .Where(s => s.RegistryId == registryId)
            .OrderBy(s => s.Name)
            .ToList();
    }

    public void UpdateScopeMap(AcrScopeMap scopeMap)
    {
        _scopeMaps[scopeMap.Id] = scopeMap;
    }

    public void RemoveScopeMap(Guid id)
    {
        _scopeMaps.TryRemove(id, out _);
    }

    // ACR Tokens
    public void AddToken(AcrToken token)
    {
        _tokens[token.Id] = token;
    }

    public AcrToken? GetToken(Guid id)
    {
        _tokens.TryGetValue(id, out var token);
        return token;
    }

    public List<AcrToken> GetTokens(Guid registryId)
    {
        return _tokens.Values
            .Where(t => t.RegistryId == registryId)
            .OrderByDescending(t => t.CreatedAt)
            .ToList();
    }

    public void UpdateToken(AcrToken token)
    {
        _tokens[token.Id] = token;
    }

    public void RemoveToken(Guid id)
    {
        _tokens.TryRemove(id, out _);
    }
}
