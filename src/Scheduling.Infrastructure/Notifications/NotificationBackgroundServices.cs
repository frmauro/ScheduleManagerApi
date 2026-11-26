using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scheduling.Application.Abstractions;

namespace Scheduling.Infrastructure.Notifications;

/// <summary>Executa um processador periodicamente em um escopo próprio; falhas são registradas e não derrubam o host.</summary>
internal abstract class PeriodicProcessorService<TProcessor>(
    IServiceScopeFactory scopeFactory,
    ILogger logger,
    Func<NotificationOptions, int> intervalSeconds,
    IOptions<NotificationOptions> options) : BackgroundService
    where TProcessor : notnull
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds(options.Value)));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await RunAsync(scope.ServiceProvider.GetRequiredService<TProcessor>(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao executar {Processor}.", typeof(TProcessor).Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    protected abstract Task RunAsync(TProcessor processor, CancellationToken cancellationToken);
}

internal sealed class ReminderSchedulerService(IServiceScopeFactory scopeFactory, ILogger<ReminderSchedulerService> logger, IOptions<NotificationOptions> options)
    : PeriodicProcessorService<IReminderProcessor>(scopeFactory, logger, o => o.ReminderIntervalSeconds, options)
{
    protected override Task RunAsync(IReminderProcessor processor, CancellationToken cancellationToken) => processor.RunOnceAsync(cancellationToken);
}

internal sealed class NotificationDispatcherService(IServiceScopeFactory scopeFactory, ILogger<NotificationDispatcherService> logger, IOptions<NotificationOptions> options)
    : PeriodicProcessorService<INotificationDispatcher>(scopeFactory, logger, o => o.DispatchIntervalSeconds, options)
{
    protected override Task RunAsync(INotificationDispatcher processor, CancellationToken cancellationToken) => processor.RunOnceAsync(cancellationToken);
}
