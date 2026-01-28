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
    
    // Container Registries
    IEnumerable<ContainerRegistry> GetRegistries();
    ContainerRegistry? GetRegistry(Guid id);
    void AddRegistry(ContainerRegistry registry);
    void RemoveRegistry(Guid id);
    
    // ACR Scope Maps
    void AddScopeMap(AcrScopeMap scopeMap);
    AcrScopeMap? GetScopeMap(Guid id);
    List<AcrScopeMap> GetScopeMaps(Guid registryId);
    void UpdateScopeMap(AcrScopeMap scopeMap);
    void RemoveScopeMap(Guid id);
    
    // ACR Tokens
    void AddToken(AcrToken token);
    AcrToken? GetToken(Guid id);
    List<AcrToken> GetTokens(Guid registryId);
    void UpdateToken(AcrToken token);
    void RemoveToken(Guid id);
    
    // Customers
    IEnumerable<Customer> GetCustomers();
    Customer? GetCustomer(string id);
    void AddCustomer(Customer customer);
    void UpdateCustomer(Customer customer);
    void DeleteCustomer(string id);
}
