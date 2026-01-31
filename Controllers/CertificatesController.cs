using Asp.Versioning;
using IamApi.Models;
using IamApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IamApi.Controllers;

[Authorize]
[ApiController]
[ApiVersion("1.0")]
[Route("api/[controller]")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class CertificatesController : ControllerBase
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

        _logger.LogInformation("Certificate request received for customer: {Customer}", customerName);

        var certificate = await _certificateService.GetOrCreateCertificateAsync(customerName);
        
        if (certificate == null)
        {
            _logger.LogWarning("Failed to create certificate for {Customer}. Check configuration and logs.", customerName);
            return StatusCode(500, new 
            { 
                error = "Failed to create certificate",
                message = "Certificate creation failed. This may be due to missing Entra ID configuration (EntraId:ClientId, EntraId:TenantId) or insufficient permissions. Check API logs for details."
            });
        }

        _logger.LogInformation("Certificate retrieved successfully for customer: {Customer}, expires: {ExpiresAt}", 
            customerName, certificate.ExpiresAt);
        return Ok(certificate);
    }
}
