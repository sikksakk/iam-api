using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace IamApi.Controllers;

[AllowAnonymous]
[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class VersionController : ControllerBase
{
    [HttpGet]
    public ActionResult<VersionInfo> GetVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var buildDate = System.IO.File.GetLastWriteTimeUtc(assembly.Location);
            
            // Read Git commit hash from version file
            var version = "1.0.0";
            var versionFile = Path.Combine(AppContext.BaseDirectory, "version.txt");
            if (System.IO.File.Exists(versionFile))
            {
                version = System.IO.File.ReadAllText(versionFile).Trim();
            }
            
            return Ok(new VersionInfo
            {
                Version = version,
                BuildDate = buildDate,
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
            });
        }
        catch
        {
            return Ok(new VersionInfo
            {
                Version = "1.0.0",
                BuildDate = DateTime.UtcNow,
                Environment = "Unknown"
            });
        }
    }
}

public sealed class VersionInfo
{
    public string Version { get; set; } = string.Empty;
    public DateTime BuildDate { get; set; }
    public string Environment { get; set; } = string.Empty;
}
