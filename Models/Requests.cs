namespace IamApi.Models;

public sealed class CreateJobRequest
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
    public JobType JobType { get; set; }
    public bool IsWhatIf { get; set; }
    public string? Schedule { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public DateTime? ScheduledFor { get; set; }
}

public sealed class CreateLogRequest
{
    public Guid JobId { get; set; }
    public string Message { get; set; } = string.Empty;
    public LogLevel Level { get; set; }
    public string? Source { get; set; }
}

public sealed class CreateCustomerRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ContactEmail { get; set; }
    public string? DefaultRegistry { get; set; }
    public string? DefaultContainerImage { get; set; }
}
