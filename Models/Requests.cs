namespace IamApi.Models;

public class CreateJobRequest
{
    public string Name { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public string ContainerImage { get; set; } = string.Empty;
    public string? RegistryServer { get; set; }
    public string? RegistryUsername { get; set; }
    public string? RegistryPassword { get; set; }
    public JobType JobType { get; set; }
    public bool IsWhatIf { get; set; }
    public string? Schedule { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
}

public class CreateLogRequest
{
    public Guid JobId { get; set; }
    public string Message { get; set; } = string.Empty;
    public LogLevel Level { get; set; }
    public string? Source { get; set; }
}
