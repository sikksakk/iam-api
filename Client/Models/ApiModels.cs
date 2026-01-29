namespace IamApi.Client.Models;

public class Orchestrator
{
    public string Id { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime LastHeartbeat { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string HostName { get; set; } = string.Empty;
    public bool? IsOnline { get; set; }
}

public class Job
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string ContainerImage { get; set; } = string.Empty;
    public string? RegistryServer { get; set; }
    public string? RegistryUsername { get; set; }
    public string? RegistryPassword { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string JobType { get; set; } = "OneOff";
    public bool IsWhatIf { get; set; }
    public bool IsPaused { get; set; }
    public string? Schedule { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ScheduledFor { get; set; }
}

public class Customer
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ContactEmail { get; set; }
    public string? DefaultRegistry { get; set; }
    public string? DefaultContainerImage { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ContainerRegistry
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ResourceGroup { get; set; }
    public string? SubscriptionId { get; set; }
    public string? AzureResourceId { get; set; }
    public bool UseGraphManagement { get; set; }
    public string Type { get; set; } = "Generic"; // "Generic" or "AzureContainerRegistry"
}

public class ContainerImage
{
    public string Repository { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public DateTime? LastUpdateTime { get; set; }
    public string Digest { get; set; } = string.Empty;
}

public class ContainerImageListResponse
{
    public string RegistryName { get; set; } = string.Empty;
    public List<ContainerImage> Images { get; set; } = new();
    public DateTime RetrievedAt { get; set; }
}

public class AcrScopeMap
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Actions { get; set; } = new();
    public Guid RegistryId { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public List<Guid> AssociatedTokenIds { get; set; } = new();
}

public class AcrToken
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
    public Guid RegistryId { get; set; }
    public Guid ScopeMapId { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public string Status { get; set; } = "Active"; // "Active", "Disabled", "Expired", "PendingDeletion"
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string? AssignedToOrchestratorId { get; set; }
    public Guid? AssignedToJobId { get; set; }
}

public class CreateScopeMapRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Repositories { get; set; } = new();
    public List<string> Actions { get; set; } = new() { "content/read" };
}

public class CreateTokenRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid ScopeMapId { get; set; }
    public int? ExpiryInDays { get; set; }
    public string? AssignToOrchestratorId { get; set; }
    public Guid? AssignToJobId { get; set; }
}

public class TokenResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public Guid ScopeMapId { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class RegistryCleanupResult
{
    public int TokensDeleted { get; set; }
    public int ScopeMapsDeleted { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class Certificate
{
    public string CustomerName { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired { get; set; }
    public int ExpiresInMinutes { get; set; }
}

public class LogEntry
{
    public Guid JobId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Level { get; set; } = "Info";
    public string Source { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public class UserInfo
{
    public string Username { get; set; } = string.Empty;
}

public class CreateJobRequest
{
    public string Name { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string ContainerImage { get; set; } = string.Empty;
    public string? RegistryServer { get; set; }
    public string? RegistryUsername { get; set; }
    public string? RegistryPassword { get; set; }
    public Guid? RegistryId { get; set; }  // ACR registry ID for automatic token creation
    public string? ImageRepository { get; set; }  // Selected repository from ACR
    public string JobType { get; set; } = "OneOff";
    public bool IsWhatIf { get; set; }
    public string? Schedule { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public DateTime? ScheduledFor { get; set; }
}

public class CreateRegistryRequest
{
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? ResourceGroup { get; set; }
    public string? SubscriptionId { get; set; }
    public bool UseGraphManagement { get; set; }
    public string Type { get; set; } = "Generic"; // "Generic" or "AzureContainerRegistry"
}

public class CleanupResult
{
    public int RemovedCount { get; set; }
}

public class CreateCustomerRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ContactEmail { get; set; }
    public string? DefaultRegistry { get; set; }
    public string? DefaultContainerImage { get; set; }
}

public class VersionInfo
{
    public string Version { get; set; } = string.Empty;
    public DateTime BuildDate { get; set; }
    public string Environment { get; set; } = string.Empty;
}
