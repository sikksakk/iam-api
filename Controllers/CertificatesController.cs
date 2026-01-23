using IamApi.Models;
using IamApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IamApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CertificatesController : ControllerBase
{
    private readonly ICertificateService _certificateService;
    private readonly ILogger<CertificatesController> _logger;

    public CertificatesController(
        ICertificateService certificateService,
        ILogger<CertificatesController> logger)
    {
        _certificateService = certificateService;
        _logger = logger;
    }

    /// <summary>
    /// Get or create a certificate for a specific customer
    /// </summary>
    [HttpGet("{customerName}")]
    public async Task<ActionResult<CertificateResponse>> GetCertificate(string customerName)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return BadRequest("Customer name is required");
        }

        var certificate = await _certificateService.GetOrCreateCertificateAsync(customerName);
        
        if (certificate == null)
        {
            return StatusCode(500, "Failed to create certificate. Check configuration.");
        }

        _logger.LogInformation("Certificate retrieved for customer: {Customer}", customerName);
        return Ok(certificate);
    }

    /// <summary>
    /// Get all active certificates
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAllCertificates()
    {
        var certificates = await _certificateService.GetAllCertificatesAsync();
        
        var response = certificates.Select(c => new
        {
            c.CustomerName,
            c.Thumbprint,
            c.CreatedAt,
            c.ExpiresAt,
            IsExpired = c.ExpiresAt < DateTime.UtcNow,
            ExpiresInMinutes = (int)(c.ExpiresAt - DateTime.UtcNow).TotalMinutes
        });

        return Ok(response);
    }

    /// <summary>
    /// Manually trigger certificate cleanup
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<ActionResult> CleanupExpiredCertificates()
    {
        await _certificateService.CleanupExpiredCertificatesAsync();
        return Ok(new { message = "Cleanup completed" });
    }
}
