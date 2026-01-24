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
            try
            {
                _logger.LogDebug("Initializing Microsoft Graph client with Managed Identity");
                
                // Log environment variables for managed identity diagnostics
                LogManagedIdentityEnvironment();
                
                // Use ManagedIdentityCredential specifically for Container Apps
                _logger.LogDebug("Creating ManagedIdentityCredential instance");
                var credential = new ManagedIdentityCredential();
                
                _logger.LogDebug("Creating GraphServiceClient with credential");
                _graphClient = new GraphServiceClient(credential);
                
                _logger.LogInformation("Graph client initialized successfully with Managed Identity");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Graph client. Ensure Managed Identity is enabled on this Container App. See CERTIFICATE-CONFIG.md for setup instructions.");
                throw new InvalidOperationException(
                    "Cannot initialize Graph client. Managed Identity not available. " +
                    "Please enable System-Assigned Managed Identity on your Azure Container App and grant it permissions to the App Registration.",
                    ex);
            }
        }
        return _graphClient;
    }

    private void LogManagedIdentityEnvironment()
    {
        try
        {
            // Log key managed identity environment variables
            var envVars = new Dictionary<string, string?>
            {
                ["IDENTITY_ENDPOINT"] = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT"),
                ["IDENTITY_HEADER"] = Environment.GetEnvironmentVariable("IDENTITY_HEADER") != null ? "[SET]" : null,
                ["MSI_ENDPOINT"] = Environment.GetEnvironmentVariable("MSI_ENDPOINT"),
                ["MSI_SECRET"] = Environment.GetEnvironmentVariable("MSI_SECRET") != null ? "[SET]" : null,
                ["AZURE_CLIENT_ID"] = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"),
                ["AZURE_TENANT_ID"] = Environment.GetEnvironmentVariable("AZURE_TENANT_ID"),
                ["AZURE_FEDERATED_TOKEN_FILE"] = Environment.GetEnvironmentVariable("AZURE_FEDERATED_TOKEN_FILE"),
                ["IMDS_ENDPOINT"] = Environment.GetEnvironmentVariable("IMDS_ENDPOINT"),
                ["CONTAINER_APP_NAME"] = Environment.GetEnvironmentVariable("CONTAINER_APP_NAME"),
                ["CONTAINER_APP_REVISION"] = Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION")
            };

            _logger.LogDebug("Managed Identity Environment Check:");
            foreach (var kvp in envVars)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    _logger.LogDebug("  {Key}: {Value}", kvp.Key, kvp.Value);
                }
                else
                {
                    _logger.LogDebug("  {Key}: [NOT SET]", kvp.Key);
                }
            }

            // Determine managed identity type
            if (!string.IsNullOrEmpty(envVars["IDENTITY_ENDPOINT"]))
            {
                _logger.LogDebug("Detected: Azure Container Apps / App Service Managed Identity (IDENTITY_ENDPOINT)");
            }
            else if (!string.IsNullOrEmpty(envVars["MSI_ENDPOINT"]))
            {
                _logger.LogDebug("Detected: Legacy MSI Endpoint");
            }
            else
            {
                _logger.LogWarning("No managed identity endpoint detected. IDENTITY_ENDPOINT and MSI_ENDPOINT are both not set.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log managed identity environment variables");
        }
    }

    public async Task<CertificateResponse?> GetOrCreateCertificateAsync(string customerName)
    {
        _logger.LogInformation("GetOrCreateCertificateAsync called for customer: {Customer}", customerName);
        
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
            var tenantId = _configuration["AzureAd:TenantId"];
            
            _logger.LogDebug("Configuration check - ClientId: {ClientId}, TenantId: {TenantId}, ValidityHours: {Hours}",
                string.IsNullOrEmpty(clientId) ? "[NOT SET]" : $"{clientId.Substring(0, Math.Min(8, clientId.Length))}...",
                string.IsNullOrEmpty(tenantId) ? "[NOT SET]" : $"{tenantId.Substring(0, Math.Min(8, tenantId.Length))}...",
                validityHours);
            
            if (string.IsNullOrEmpty(clientId))
            {
                _logger.LogError("AzureAd:ClientId not configured. Certificate management requires Azure AD configuration. Set AzureAd:ClientId and AzureAd:TenantId in configuration.");
                return null;
            }
            
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("AzureAd:TenantId not configured, but will attempt to proceed");
            }

            _logger.LogInformation("Creating new certificate for customer: {Customer}", customerName);

            // Generate certificate
            _logger.LogDebug("Generating self-signed certificate with {Hours} hour validity", validityHours);
            var certPassword = GenerateSecurePassword();
            var (pfxBytes, thumbprint, certificate) = GenerateSelfSignedCertificate(
                $"CN=IAM-{customerName}",
                validityHours,
                certPassword);
            
            _logger.LogDebug("Certificate generated - Thumbprint: {Thumbprint}, Size: {Size} bytes", thumbprint, pfxBytes.Length);

            // Upload to Azure AD App Registration
            _logger.LogDebug("Attempting to upload certificate to Azure AD for ClientId: {ClientId}", clientId);
            var keyId = await UploadCertificateToAzureAsync(clientId, certificate);
            _logger.LogDebug("Certificate uploaded successfully with KeyId: {KeyId}", keyId);

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
            _logger.LogError(ex, "Failed to create certificate for {Customer}. Error type: {Type}, Message: {Message}", 
                customerName, ex.GetType().Name, ex.Message);
            if (ex.InnerException != null)
            {
                _logger.LogError("Inner exception: {InnerType} - {InnerMessage}", 
                    ex.InnerException.GetType().Name, ex.InnerException.Message);
            }
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
            _logger.LogDebug("Getting Graph client for certificate upload");
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

            _logger.LogDebug("Fetching application {ClientId} from Azure AD", clientId);
            _logger.LogDebug("Making Graph API call: GET /applications/{ClientId}", clientId);
            
            // Get current application
            var application = await graphClient.Applications[clientId].GetAsync();
            
            _logger.LogDebug("Graph API call successful. Application retrieved: {AppId}", application?.AppId);
            
            if (application == null)
            {
                _logger.LogError("Application {ClientId} not found in Azure AD", clientId);
                throw new InvalidOperationException($"Application {clientId} not found. Verify the ClientId is correct (use Application/Client ID, not Object ID).");
            }
            
            if (application.KeyCredentials == null)
            {
                application.KeyCredentials = new List<KeyCredential>();
            }

            _logger.LogDebug("Adding certificate to application. Current certificates: {Count}", application.KeyCredentials.Count);
            
            // Add new certificate
            application.KeyCredentials.Add(keyCredential);

            // Update application
            _logger.LogDebug("Updating application in Azure AD");
            await graphClient.Applications[clientId].PatchAsync(application);

            _logger.LogInformation("Certificate uploaded to Azure AD for app {ClientId}", clientId);
            
            return keyCredential.KeyId?.ToString() ?? Guid.NewGuid().ToString();
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw our custom exceptions
        }
        catch (Azure.Identity.CredentialUnavailableException ex)
        {
            _logger.LogError(ex, "Managed Identity not available. Ensure System-Assigned Managed Identity is enabled on the Container App.");
            throw new InvalidOperationException(
                "Managed Identity authentication failed. " +
                "Please enable System-Assigned Managed Identity on your Azure Container App in the Identity settings.",
                ex);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            _logger.LogError(ex, "Permission denied. Managed Identity needs Application.ReadWrite.All or Owner role on the App Registration.");
            throw new InvalidOperationException(
                "Permission denied when accessing Azure AD. " +
                "Grant the Container App's Managed Identity 'Application Administrator' role or 'Owner' role on the App Registration.",
                ex);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            _logger.LogError(ex, "Application {ClientId} not found. Verify the ClientId configuration.", clientId);
            throw new InvalidOperationException(
                $"App Registration with ClientId {clientId} not found. " +
                "Verify AzureAd:ClientId is set to the Application (client) ID from the App Registration overview.",
                ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error uploading certificate to Azure AD. Type: {ExceptionType}", ex.GetType().Name);
            _logger.LogError("Exception details - Message: {Message}", ex.Message);
            if (ex.InnerException != null)
            {
                _logger.LogError("Inner exception type: {InnerType}, Message: {InnerMessage}", 
                    ex.InnerException.GetType().FullName, ex.InnerException.Message);
                
                // Log MSAL specific details if available
                if (ex.InnerException is Microsoft.Identity.Client.MsalServiceException msalEx)
                {
                    _logger.LogError("MSAL Error Code: {ErrorCode}", msalEx.ErrorCode);
                    _logger.LogError("MSAL Correlation ID: {CorrelationId}", msalEx.CorrelationId);
                    _logger.LogError("MSAL Status Code: {StatusCode}", msalEx.StatusCode);
                    _logger.LogError("MSAL Response Body: {ResponseBody}", msalEx.ResponseBody);
                    _logger.LogError("MSAL Claims: {Claims}", msalEx.Claims);
                }
            }
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
