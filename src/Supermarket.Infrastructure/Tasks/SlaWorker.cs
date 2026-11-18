using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Task = System.Threading.Tasks.Task;
namespace Supermarket.Infrastructure.Tasks;

public sealed class SlaWorkerOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>How often open Tasks and Incidents are checked for SLA warning and breach milestones.</summary>
    public int PollSeconds { get; set; } = 15;
}

/// <summary>Raises SLA warning/breach milestones for open Tasks and Incidents (BR-38–BR-41).</summary>
public sealed class SlaWorker(IServiceScopeFactory scopes, IOptions<SlaWorkerOptions> options, ILogger<SlaWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollSeconds, 1, 300)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SlaMonitor>().Tick(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            // Log the type only: SQL messages can contain connection details.
            catch (Exception e) { log.LogWarning("SLA milestone check failed ({ErrorType})", e.GetType().Name); }
        }
    }
}
