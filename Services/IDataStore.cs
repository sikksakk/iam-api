using IamApi.Models;

namespace IamApi.Services;

public interface IDataStore
{
    // Jobs
    Job AddJob(Job job);
    Job? GetJob(Guid id);
    IEnumerable<Job> GetAllJobs();
    IEnumerable<Job> GetPendingJobs();
    Job? UpdateJobStatus(Guid id, JobStatus status);
    Job? UpdateJob(Job job);
    bool DeleteJob(Guid id);
    
    // Logs
    LogEntry AddLog(LogEntry log);
    IEnumerable<LogEntry> GetLogs(Guid? jobId = null);
    IEnumerable<LogEntry> GetLogsByJob(Guid jobId);
    
    // Orchestrators
    IEnumerable<Orchestrator> GetOrchestrators();
    void UpsertOrchestrator(Orchestrator orchestrator);
    void RemoveOrchestrator(string id);
}
