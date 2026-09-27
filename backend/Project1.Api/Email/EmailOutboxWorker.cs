using Microsoft.Extensions.Options;

namespace Project1.Api.Email;

public sealed class EmailOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SmtpOptions> options,
    ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    private readonly TimeSpan pollInterval = TimeSpan.FromSeconds(
        Math.Max(1, options.Value.PollIntervalSeconds));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IEmailOutboxProcessor>();
                var processed = await processor.ProcessNextAsync(stoppingToken);
                if (processed)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The email outbox worker encountered an unexpected error.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}
