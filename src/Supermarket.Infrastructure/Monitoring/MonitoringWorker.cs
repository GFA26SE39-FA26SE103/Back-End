using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
namespace Supermarket.Infrastructure.Monitoring;
public sealed class MonitoringWorker(MonitoringCameraCoordinator coordinator,IOptions<MonitoringWorkerOptions> configured):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!configured.Value.Enabled) return;
        while(!stoppingToken.IsCancellationRequested)
        {
            await coordinator.Tick(stoppingToken);
            await Task.Delay(Math.Clamp(configured.Value.PollIntervalMs,50,60000),stoppingToken);
        }
    }
    public override async Task StopAsync(CancellationToken ct)
    { await base.StopAsync(ct); await coordinator.Shutdown(ct); }
}
