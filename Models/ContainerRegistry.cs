namespace IamApi.Models;

public sealed class ContainerRegistry
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    
    // Azure Container Registry properties
    public string? ResourceGroup { get; set; }
    public string? SubscriptionId { get; set; }
    public string? AzureResourceId { get; set; }
    public bool UseGraphManagement { get; set; } = false;
    public RegistryType Type { get; set; } = RegistryType.Generic;
}

public enum RegistryType
{
    Generic,
    AzureContainerRegistry
}

public sealed class CreateRegistryRequest
{
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? ResourceGroup { get; set; }
    public string? SubscriptionId { get; set; }
    public bool UseGraphManagement { get; set; } = false;
    public RegistryType Type { get; set; } = RegistryType.Generic;
}
