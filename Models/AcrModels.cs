namespace IamApi.Models;

/// <summary>
/// Represents a container image repository
/// </summary>
public class ContainerImage
{
    public string Repository { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public DateTime? LastUpdateTime { get; set; }
    public string Digest { get; set; } = string.Empty;
}

/// <summary>
/// Represents an ACR scope map that defines access permissions
/// </summary>
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
    
    // Reference tracking
    public List<Guid> AssociatedTokenIds { get; set; } = new();
}

/// <summary>
/// Represents an ACR token for authentication
/// </summary>
public class AcrToken
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
    public Guid RegistryId { get; set; }
    public Guid ScopeMapId { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public TokenStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    
    // Reference tracking
    public string? AssignedToOrchestratorId { get; set; }
    public Guid? AssignedToJobId { get; set; }
}

public enum TokenStatus
{
    Active,
    Disabled,
    Expired,
    PendingDeletion
}

// Request/Response DTOs
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
    public TokenStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class ContainerImageListResponse
{
    public string RegistryName { get; set; } = string.Empty;
    public List<ContainerImage> Images { get; set; } = new();
    public DateTime RetrievedAt { get; set; }
}
