using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
namespace Supermarket.Infrastructure;

public sealed class HealthWorkerOptions
{
    public bool Enabled { get; set; } = true; public int IntervalSeconds { get; set; } = 30;
}
public sealed class CameraHealthWorker(IServiceScopeFactory scopes, IOptions<HealthWorkerOptions> options, ILogger<CameraHealthWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.IntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var listScope = scopes.CreateScope();
                var enabled = await listScope.ServiceProvider.GetRequiredService<ISetupStore>().List<CameraConnection>(c => c.IsEnabled, stoppingToken);
                foreach (var connection in enabled)
                {
                    using var scope = scopes.CreateScope();
                    try
                    {
                        await scope.ServiceProvider.GetRequiredService<CameraHealth>().CheckInternal(connection.CameraId, stoppingToken);
                    }
                    catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning("Camera health check failed for {CameraId} ({ErrorType})", connection.CameraId, e.GetType().Name); }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning("Health monitoring iteration failed ({ErrorType})", e.GetType().Name); }
        }
    }
}
