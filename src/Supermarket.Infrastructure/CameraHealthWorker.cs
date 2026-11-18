using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
namespace Supermarket.Infrastructure;

public sealed class HealthWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
    public int MaxConcurrentChecks { get; set; } = 2;
    public int BadObservationsToOpen { get; set; } = 3;
    public int GoodObservationsToResolve { get; set; } = 2;
}
public sealed class CameraHealthWorker(IServiceScopeFactory scopes, IOptions<HealthWorkerOptions> options, ILogger<CameraHealthWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;
        await RunIteration(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunIteration(stoppingToken);
    }

    private async Task RunIteration(CancellationToken stoppingToken)
    {
        try
        {
            using var listScope = scopes.CreateScope();
            var enabled = await listScope.ServiceProvider.GetRequiredService<ISetupStore>().List<CameraConnection>(c => c.IsEnabled, stoppingToken);
            enabled = enabled.Where(connection =>
            {
                try { Rules.Connection(connection); return true; }
                catch (DomainException) { return false; }
            }).ToList();
            await Parallel.ForEachAsync(enabled, new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Value.MaxConcurrentChecks,
                CancellationToken = stoppingToken
            }, async (connection, ct) =>
            {
                using var scope = scopes.CreateScope();
                try
                {
                    await scope.ServiceProvider.GetRequiredService<CameraHealth>().CheckInternal(connection.CameraId, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogWarning("Camera health check failed for {CameraId} ({ErrorType})", connection.CameraId, e.GetType().Name);
                }
            });
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("Health monitoring iteration failed ({ErrorType})", e.GetType().Name);
        }
    }
}
