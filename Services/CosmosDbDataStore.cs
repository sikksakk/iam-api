using System.Net;
using IamApi.Models;
using Microsoft.Azure.Cosmos;

namespace IamApi.Services;

public class CosmosDbDataStore : IDataStore
{
    private readonly ILogger<CosmosDbDataStore> _logger;
    private readonly CosmosClient _cosmosClient;
    private readonly Database _database;
    private readonly Container _jobsContainer;
    private readonly Container _logsContainer;
    private readonly Container _orchestratorsContainer;
    private readonly Container _registriesContainer;
    private readonly Container _customersContainer;

    public CosmosDbDataStore(ILogger<CosmosDbDataStore> logger, string connectionString, string databaseName)
    {
        _logger = logger;
        
        _logger.LogInformation("Initializing Cosmos DB data store...");
        
        // Validate connection string
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogError("Cosmos DB connection string is null or empty");
            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));
        }
        
        _logger.LogDebug("Connection string length: {Length} characters", connectionString.Length);
        
        // Validate connection string format - must contain AccountEndpoint and AccountKey
        if (!connectionString.Contains("AccountEndpoint=", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Invalid Cosmos DB connection string: Missing 'AccountEndpoint' property");
            _logger.LogError("Expected format: 'AccountEndpoint=https://your-account.documents.azure.com:443/;AccountKey=...;'");
            _logger.LogError("Received connection string starts with: {Start}...", 
                connectionString.Length > 50 ? connectionString.Substring(0, 50) : connectionString);
            throw new ArgumentException(
                "Invalid Cosmos DB connection string. Missing required 'AccountEndpoint' property. " +
                "Expected format: 'AccountEndpoint=https://...;AccountKey=...;'", 
                nameof(connectionString));
        }
        
        if (!connectionString.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Invalid Cosmos DB connection string: Missing 'AccountKey' property");
            throw new ArgumentException(
                "Invalid Cosmos DB connection string. Missing required 'AccountKey' property. " +
                "Expected format: 'AccountEndpoint=https://...;AccountKey=...;'", 
                nameof(connectionString));
        }
        
        // Log connection info (mask sensitive parts)
        var accountEndpoint = ExtractAccountEndpoint(connectionString);
        _logger.LogInformation("Cosmos DB Account Endpoint: {Endpoint}", accountEndpoint ?? "[Unable to parse]");
        
        try
        {
            _logger.LogDebug("Creating CosmosClient with DirectMode connection...");
            var cosmosClientOptions = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                },
                ConnectionMode = ConnectionMode.Direct,
                RequestTimeout = TimeSpan.FromSeconds(30)
            };

            _cosmosClient = new CosmosClient(connectionString, cosmosClientOptions);
            _logger.LogInformation("✓ CosmosClient created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create CosmosClient. Check connection string format.");
            throw;
        }
        
        // Initialize database and containers
        try
        {
            _logger.LogInformation("Initializing database '{DatabaseName}'...", databaseName);
            _database = InitializeDatabaseAsync(databaseName).GetAwaiter().GetResult();
            
            _logger.LogInformation("Initializing containers...");
            _jobsContainer = InitializeContainerAsync("Jobs", "/id").GetAwaiter().GetResult();
            _logsContainer = InitializeContainerAsync("Logs", "/jobId").GetAwaiter().GetResult();
            _orchestratorsContainer = InitializeContainerAsync("Orchestrators", "/id").GetAwaiter().GetResult();
            _registriesContainer = InitializeContainerAsync("Registries", "/id").GetAwaiter().GetResult();
            _customersContainer = InitializeContainerAsync("Customers", "/id").GetAwaiter().GetResult();
            
            _logger.LogInformation("✓ Cosmos DB initialized successfully - Database: {DatabaseName}, Containers: 5", databaseName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Cosmos DB database or containers");
            throw;
        }
    }
    
    private string? ExtractAccountEndpoint(string connectionString)
    {
        try
        {
            var parts = connectionString.Split(';');
            var endpointPart = parts.FirstOrDefault(p => p.Trim().StartsWith("AccountEndpoint=", StringComparison.OrdinalIgnoreCase));
            if (endpointPart != null)
            {
                return endpointPart.Split('=', 2)[1].Trim();
            }
        }
        catch
        {
            // Ignore parsing errors
        }
        return null;
    }

    private async Task<Database> InitializeDatabaseAsync(string databaseName)
    {
        _logger.LogDebug("Checking if database '{DatabaseName}' exists...", databaseName);
        
        try
        {
            var response = await _cosmosClient.CreateDatabaseIfNotExistsAsync(databaseName);
            
            if (response.StatusCode == HttpStatusCode.Created)
            {
                _logger.LogInformation("✓ Database '{DatabaseName}' created (Cost: {RU} RU)", 
                    databaseName, response.RequestCharge);
            }
            else if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.LogInformation("✓ Database '{DatabaseName}' already exists (Cost: {RU} RU)", 
                    databaseName, response.RequestCharge);
            }
            
            return response.Database;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos DB error creating database '{DatabaseName}' - StatusCode: {StatusCode}, Message: {Message}", 
                databaseName, ex.StatusCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating database '{DatabaseName}'", databaseName);
            throw;
        }
    }

    private async Task<Container> InitializeContainerAsync(string containerName, string partitionKeyPath)
    {
        _logger.LogDebug("Checking if container '{ContainerName}' exists with partition key '{PartitionKey}'...", 
            containerName, partitionKeyPath);
        
        try
        {
            var containerProperties = new ContainerProperties
            {
                Id = containerName,
                PartitionKeyPath = partitionKeyPath
            };

            // Don't specify throughput - works for both serverless and provisioned accounts
            // Serverless: Auto-scales (throughput not allowed)
            // Provisioned: Uses database-level throughput or defaults to minimum (400 RU/s)
            var response = await _database.CreateContainerIfNotExistsAsync(containerProperties);
            
            if (response.StatusCode == HttpStatusCode.Created)
            {
                _logger.LogInformation("  ✓ Container '{ContainerName}' created (Partition: {PartitionKey}, Cost: {RU} RU)", 
                    containerName, partitionKeyPath, response.RequestCharge);
            }
            else if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.LogInformation("  ✓ Container '{ContainerName}' already exists (Partition: {PartitionKey}, Cost: {RU} RU)", 
                    containerName, partitionKeyPath, response.RequestCharge);
            }
            
            return response.Container;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Cosmos DB error creating container '{ContainerName}' - StatusCode: {StatusCode}, Message: {Message}", 
                containerName, ex.StatusCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating container '{ContainerName}'", containerName);
            throw;
        }
    }

    // Jobs
    public Job AddJob(Job job)
    {
        job.Id = Guid.NewGuid();
        job.CreatedAt = DateTime.UtcNow;
        job.Status = JobStatus.Pending;
        
        _logger.LogInformation("Creating job {JobId} with status {Status} ({StatusInt})", 
            job.Id, job.Status, (int)job.Status);
        
        try
        {
            _jobsContainer.CreateItemAsync(job, new PartitionKey(job.Id.ToString())).GetAwaiter().GetResult();
            _logger.LogInformation("Job {JobId} ({JobName}) created successfully in Cosmos DB", job.Id, job.Name);
            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create job {JobId} in Cosmos DB", job.Id);
            throw;
        }
    }

    public Job? GetJob(Guid id)
    {
        try
        {
            var response = _jobsContainer.ReadItemAsync<Job>(id.ToString(), new PartitionKey(id.ToString()))
                .GetAwaiter().GetResult();
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public IEnumerable<Job> GetAllJobs()
    {
        _logger.LogInformation("Querying Cosmos DB for all jobs...");
        
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.createdAt DESC");
        var iterator = _jobsContainer.GetItemQueryIterator<Job>(query);
        var jobs = new List<Job>();

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            _logger.LogDebug("GetAllJobs page returned {Count} jobs", response.Count);
            jobs.AddRange(response);
        }

        _logger.LogInformation("Retrieved {Count} total jobs from Cosmos DB. Status breakdown: {StatusBreakdown}", 
            jobs.Count,
            string.Join(", ", jobs.GroupBy(j => j.Status).Select(g => $"{g.Key}={g.Count()}")));
        
        return jobs;
    }

    public IEnumerable<Job> GetPendingJobs()
    {
        _logger.LogInformation("Querying Cosmos DB for pending jobs...");
        
        // Try both string and integer representations of the enum
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.status = @statusString OR c.status = @statusInt ORDER BY c.createdAt ASC")
            .WithParameter("@statusString", JobStatus.Pending.ToString())
            .WithParameter("@statusInt", (int)JobStatus.Pending);
        
        _logger.LogInformation("Querying with status = '{StatusString}' OR {StatusInt}", 
            JobStatus.Pending.ToString(), (int)JobStatus.Pending);
        
        var iterator = _jobsContainer.GetItemQueryIterator<Job>(query);
        var jobs = new List<Job>();

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            _logger.LogInformation("Query page returned {Count} jobs (RU: {RU})", response.Count, response.RequestCharge);
            jobs.AddRange(response);
        }

        _logger.LogInformation("Found {Count} pending jobs in Cosmos DB", jobs.Count);
        return jobs;
    }

    public Job? UpdateJobStatus(Guid id, JobStatus status)
    {
        try
        {
            var job = GetJob(id);
            if (job == null) return null;

            job.Status = status;
            
            if (status == JobStatus.Running && !job.StartedAt.HasValue)
            {
                job.StartedAt = DateTime.UtcNow;
            }
            else if ((status == JobStatus.Completed || status == JobStatus.Failed) && !job.CompletedAt.HasValue)
            {
                job.CompletedAt = DateTime.UtcNow;
            }

            var response = _jobsContainer.ReplaceItemAsync(job, job.Id.ToString(), new PartitionKey(job.Id.ToString()))
                .GetAwaiter().GetResult();
            
            return response.Resource;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update job status for {JobId}", id);
            return null;
        }
    }

    public Job? UpdateJob(Job job)
    {
        try
        {
            var response = _jobsContainer.ReplaceItemAsync(job, job.Id.ToString(), new PartitionKey(job.Id.ToString()))
                .GetAwaiter().GetResult();
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public bool DeleteJob(Guid id)
    {
        try
        {
            _jobsContainer.DeleteItemAsync<Job>(id.ToString(), new PartitionKey(id.ToString()))
                .GetAwaiter().GetResult();
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    // Logs
    public LogEntry AddLog(LogEntry log)
    {
        log.Id = Guid.NewGuid();
        log.Timestamp = DateTime.UtcNow;
        
        try
        {
            _logsContainer.CreateItemAsync(log, new PartitionKey(log.JobId.ToString())).GetAwaiter().GetResult();
            return log;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create log entry for job {JobId}", log.JobId);
            throw;
        }
    }

    public IEnumerable<LogEntry> GetLogs(Guid? jobId = null)
    {
        QueryDefinition query;
        if (jobId.HasValue)
        {
            query = new QueryDefinition("SELECT * FROM c WHERE c.jobId = @jobId ORDER BY c.timestamp DESC")
                .WithParameter("@jobId", jobId.Value.ToString());
        }
        else
        {
            query = new QueryDefinition("SELECT * FROM c ORDER BY c.timestamp DESC");
        }

        var iterator = _logsContainer.GetItemQueryIterator<LogEntry>(query);
        var logs = new List<LogEntry>();

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            logs.AddRange(response);
        }

        return logs;
    }

    public IEnumerable<LogEntry> GetLogsByJob(Guid jobId)
    {
        return GetLogs(jobId).OrderBy(l => l.Timestamp);
    }

    // Orchestrators
    public IEnumerable<Orchestrator> GetOrchestrators()
    {
        var query = new QueryDefinition("SELECT * FROM c");
        var iterator = _orchestratorsContainer.GetItemQueryIterator<Orchestrator>(query);
        var orchestrators = new List<Orchestrator>();

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            orchestrators.AddRange(response);
        }

        return orchestrators;
    }

    public void UpsertOrchestrator(Orchestrator orchestrator)
    {
        try
        {
            _orchestratorsContainer.UpsertItemAsync(orchestrator, new PartitionKey(orchestrator.Id))
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert orchestrator {OrchestratorId}", orchestrator.Id);
        }
    }

    public void RemoveOrchestrator(string id)
    {
        try
        {
            _orchestratorsContainer.DeleteItemAsync<Orchestrator>(id, new PartitionKey(id))
                .GetAwaiter().GetResult();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already deleted
        }
    }

    // Container Registries
    public IEnumerable<ContainerRegistry> GetRegistries()
    {
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.name ASC");
        var iterator = _registriesContainer.GetItemQueryIterator<ContainerRegistry>(query);
        var registries = new List<ContainerRegistry>();

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            registries.AddRange(response);
        }

        return registries;
    }

    public ContainerRegistry? GetRegistry(Guid id)
    {
        try
        {
            var response = _registriesContainer.ReadItemAsync<ContainerRegistry>(id.ToString(), new PartitionKey(id.ToString()))
                .GetAwaiter().GetResult();
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public void AddRegistry(ContainerRegistry registry)
    {
        try
        {
            _registriesContainer.CreateItemAsync(registry, new PartitionKey(registry.Id.ToString()))
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add registry {RegistryId}", registry.Id);
        }
    }

    public void RemoveRegistry(Guid id)
    {
        try
        {
            _registriesContainer.DeleteItemAsync<ContainerRegistry>(id.ToString(), new PartitionKey(id.ToString()))
                .GetAwaiter().GetResult();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already deleted
        }
    }

    // Customers
    public IEnumerable<Customer> GetCustomers()
    {
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.name ASC");
        var iterator = _customersContainer.GetItemQueryIterator<Customer>(query);
        var customers = new List<Customer>();

        while (iterator.HasMoreResults)
        {
            var response = iterator.ReadNextAsync().GetAwaiter().GetResult();
            customers.AddRange(response);
        }

        return customers;
    }

    public Customer? GetCustomer(string id)
    {
        try
        {
            var response = _customersContainer.ReadItemAsync<Customer>(id, new PartitionKey(id))
                .GetAwaiter().GetResult();
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public void AddCustomer(Customer customer)
    {
        try
        {
            _customersContainer.CreateItemAsync(customer, new PartitionKey(customer.Id))
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add customer {CustomerId}", customer.Id);
        }
    }

    public void UpdateCustomer(Customer customer)
    {
        try
        {
            _customersContainer.UpsertItemAsync(customer, new PartitionKey(customer.Id))
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update customer {CustomerId}", customer.Id);
        }
    }

    public void DeleteCustomer(string id)
    {
        try
        {
            _customersContainer.DeleteItemAsync<Customer>(id, new PartitionKey(id))
                .GetAwaiter().GetResult();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already deleted
        }
    }
}
