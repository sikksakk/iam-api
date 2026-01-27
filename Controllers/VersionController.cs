using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace IamApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VersionController : ControllerBase
{
    [HttpGet]
    public ActionResult<VersionInfo> GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "1.0.0";
        var buildDate = System.IO.File.GetLastWriteTimeUtc(assembly.Location);
        
        return Ok(new VersionInfo
        {
            Version = version,
            BuildDate = buildDate,
            Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
        });
    }
}

public class VersionInfo
{
    public string Version { get; set; } = string.Empty;
    public DateTime BuildDate { get; set; }
    public string Environment { get; set; } = string.Empty;
}
