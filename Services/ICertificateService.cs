using IamApi.Models;

namespace IamApi.Services;

public interface ICertificateService
{
    Task<CertificateResponse?> GetOrCreateCertificateAsync(string customerName);
    Task<CertificateResponse?> GetCertificateAsync(string customerName);
    Task CleanupExpiredCertificatesAsync();
    Task CleanupUnusedCertificatesAsync();
    Task<List<CustomerCertificate>> GetAllCertificatesAsync();
}
