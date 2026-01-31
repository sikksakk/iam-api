using IamApi.Services;

namespace IamApi;

public class CertificateMaintenanceWorker : BackgroundService
{
    private readonly ILogger<CertificateMaintenanceWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

    public CertificateMaintenanceWorker(
        ILogger<CertificateMaintenanceWorker> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Certificate Maintenance Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var certificateService = scope.ServiceProvider.GetRequiredService<ICertificateService>();

                _logger.LogDebug("Running certificate cleanup...");
                await certificateService.CleanupExpiredCertificatesAsync();
                await certificateService.CleanupUnusedCertificatesAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in certificate maintenance worker");
            }
        }

        _logger.LogInformation("Certificate Maintenance Worker stopped");
    }
}
