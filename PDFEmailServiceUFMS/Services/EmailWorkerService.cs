using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PDFEmailServiceUFMS.Repositories.IRepository;

namespace PDFEmailServiceUFMS.Services;

public class EmailWorkerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<EmailWorkerService> _logger;

    public EmailWorkerService(
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime applicationLifetime,
        ILogger<EmailWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UFMS Certificate Email Service starting at {Time}", DateTimeOffset.Now.ToString("dd-MMM-yyyy HH:mm:ss zzz"));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var workflow = scope.ServiceProvider.GetRequiredService<IEmailWorkflowService>();

            await workflow.ExecuteAsync(stoppingToken);

            _logger.LogInformation("UFMS Certificate Email Service completed successfully at {Time}", DateTimeOffset.Now.ToString("dd-MMM-yyyy HH:mm:ss zzz"));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("UFMS Certificate Email Service was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UFMS Certificate Email Service failed with error");
        }
        finally
        {
            _logger.LogInformation("UFMS Certificate Email Service shutting down");
            _applicationLifetime.StopApplication();
        }
    }
}
