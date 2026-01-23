using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using IamApi.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace IamApi.Services;

public class CertificateService : ICertificateService
{
    private readonly ILogger<CertificateService> _logger;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, CustomerCertificate> _certificates;
    private readonly SemaphoreSlim _certificateLock = new(1, 1);
    private GraphServiceClient? _graphClient;

    public CertificateService(ILogger<CertificateService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _certificates = new Dictionary<string, CustomerCertificate>();
    }

    private GraphServiceClient GetGraphClient()
    {
        if (_graphClient == null)
        {
            var credential = new DefaultAzureCredential();
            _graphClient = new GraphServiceClient(credential);
        }
        return _graphClient;
    }

    public async Task<CertificateResponse?> GetOrCreateCertificateAsync(string customerName)
    {
        await _certificateLock.WaitAsync();
        try
        {
            // Check if we have a valid certificate
            if (_certificates.TryGetValue(customerName, out var existingCert))
            {
                if (existingCert.ExpiresAt > DateTime.UtcNow.AddMinutes(10))
                {
                    _logger.LogInformation("Returning existing certificate for {Customer}, expires at {ExpiresAt}",
                        customerName, existingCert.ExpiresAt);
                    
                    return new CertificateResponse
                    {
                        CustomerName = customerName,
                        CertificateData = existingCert.CertificateData,
                        Thumbprint = existingCert.Thumbprint,
                        ExpiresAt = existingCert.ExpiresAt,
                        Password = string.Empty // Password not stored, regenerate if needed
                    };
                }
                else
                {
                    _logger.LogInformation("Certificate for {Customer} is expiring soon, creating new one", customerName);
                    await DeleteCertificateFromAzureAsync(existingCert.KeyId);
                }
            }

            // Create new certificate
            return await CreateNewCertificateAsync(customerName);
        }
        finally
        {
            _certificateLock.Release();
        }
    }

    public async Task<CertificateResponse?> GetCertificateAsync(string customerName)
    {
        if (_certificates.TryGetValue(customerName, out var cert))
        {
            if (cert.ExpiresAt > DateTime.UtcNow)
            {
                return new CertificateResponse
                {
                    CustomerName = customerName,
                    CertificateData = cert.CertificateData,
                    Thumbprint = cert.Thumbprint,
                    ExpiresAt = cert.ExpiresAt,
                    Password = string.Empty
                };
            }
        }
        
        // If no valid certificate, create one
        return await GetOrCreateCertificateAsync(customerName);
    }

    private async Task<CertificateResponse?> CreateNewCertificateAsync(string customerName)
    {
        try
        {
            var validityHours = _configuration.GetValue("AzureAd:CertificateValidityHours", 2);
            var clientId = _configuration["AzureAd:ClientId"];
            
            if (string.IsNullOrEmpty(clientId))
            {
                _logger.LogWarning("AzureAd:ClientId not configured, certificate management disabled");
                return null;
            }

            _logger.LogInformation("Creating new certificate for customer: {Customer}", customerName);

            // Generate certificate
            var certPassword = GenerateSecurePassword();
            var (pfxBytes, thumbprint, certificate) = GenerateSelfSignedCertificate(
                $"CN=IAM-{customerName}",
                validityHours,
                certPassword);

            // Upload to Azure AD App Registration
            var keyId = await UploadCertificateToAzureAsync(clientId, certificate);

            // Store certificate
            var customerCert = new CustomerCertificate
            {
                CustomerName = customerName,
                CertificateData = Convert.ToBase64String(pfxBytes),
                Thumbprint = thumbprint,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(validityHours),
                KeyId = keyId
            };

            _certificates[customerName] = customerCert;

            _logger.LogInformation("Certificate created for {Customer}, thumbprint: {Thumbprint}, expires: {ExpiresAt}",
                customerName, thumbprint, customerCert.ExpiresAt);

            return new CertificateResponse
            {
                CustomerName = customerName,
                CertificateData = customerCert.CertificateData,
                Thumbprint = thumbprint,
                ExpiresAt = customerCert.ExpiresAt,
                Password = certPassword
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create certificate for {Customer}", customerName);
            return null;
        }
    }

    private (byte[] pfxBytes, string thumbprint, X509Certificate2 certificate) GenerateSelfSignedCertificate(
        string subjectName, 
        int validityHours, 
        string password)
    {
        using var rsa = RSA.Create(2048);
        
        var request = new CertificateRequest(
            subjectName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add key usage
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        // Add enhanced key usage for client authentication
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, // Client Authentication
                critical: true));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddHours(validityHours);

        var certificate = request.CreateSelfSigned(notBefore, notAfter);
        
        // Export as PFX with password
        var pfxBytes = certificate.Export(X509ContentType.Pfx, password);
        var thumbprint = certificate.Thumbprint;

        return (pfxBytes, thumbprint, certificate);
    }

    private async Task<string> UploadCertificateToAzureAsync(string clientId, X509Certificate2 certificate)
    {
        try
        {
            var graphClient = GetGraphClient();
            
            // Create key credential
            var keyCredential = new KeyCredential
            {
                Type = "AsymmetricX509Cert",
                Usage = "Verify",
                Key = certificate.GetRawCertData(),
                DisplayName = $"IAM-Cert-{DateTime.UtcNow:yyyyMMddHHmmss}",
                EndDateTime = certificate.NotAfter.ToUniversalTime()
            };

            // Get current application
            var application = await graphClient.Applications[clientId].GetAsync();
            
            if (application?.KeyCredentials == null)
            {
                application!.KeyCredentials = new List<KeyCredential>();
            }

            // Add new certificate
            application.KeyCredentials.Add(keyCredential);

            // Update application
            await graphClient.Applications[clientId].PatchAsync(application);

            _logger.LogInformation("Certificate uploaded to Azure AD for app {ClientId}", clientId);
            
            return keyCredential.KeyId?.ToString() ?? Guid.NewGuid().ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload certificate to Azure AD");
            throw;
        }
    }

    private async Task DeleteCertificateFromAzureAsync(string keyId)
    {
        try
        {
            if (string.IsNullOrEmpty(keyId))
                return;

            var clientId = _configuration["AzureAd:ClientId"];
            if (string.IsNullOrEmpty(clientId))
                return;

            var graphClient = GetGraphClient();
            var application = await graphClient.Applications[clientId].GetAsync();

            if (application?.KeyCredentials != null)
            {
                var keyToRemove = application.KeyCredentials.FirstOrDefault(k => k.KeyId.ToString() == keyId);
                if (keyToRemove != null)
                {
                    application.KeyCredentials.Remove(keyToRemove);
                    await graphClient.Applications[clientId].PatchAsync(application);
                    _logger.LogInformation("Deleted certificate {KeyId} from Azure AD", keyId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete certificate {KeyId} from Azure AD", keyId);
        }
    }

    public async Task CleanupExpiredCertificatesAsync()
    {
        await _certificateLock.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            var expiredCerts = _certificates.Where(kvp => kvp.Value.ExpiresAt < now).ToList();

            foreach (var expired in expiredCerts)
            {
                _logger.LogInformation("Cleaning up expired certificate for {Customer}", expired.Key);
                await DeleteCertificateFromAzureAsync(expired.Value.KeyId);
                _certificates.Remove(expired.Key);
            }

            if (expiredCerts.Any())
            {
                _logger.LogInformation("Cleaned up {Count} expired certificates", expiredCerts.Count);
            }
        }
        finally
        {
            _certificateLock.Release();
        }
    }

    public async Task<List<CustomerCertificate>> GetAllCertificatesAsync()
    {
        await Task.CompletedTask;
        return _certificates.Values.ToList();
    }

    private static string GenerateSecurePassword()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        var random = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(random);
        }
        
        return new string(random.Select(b => chars[b % chars.Length]).ToArray());
    }
}
