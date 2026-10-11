using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Task = System.Threading.Tasks.Task;
namespace Supermarket.Infrastructure.Tasks;

public sealed class TaskOfferWorkerOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>How often pending offers are checked; the retry interval itself comes from NotificationConfig (default 60 s).</summary>
    public int PollSeconds { get; set; } = 10;
}

/// <summary>Detects Database V2 task tables plus the V3 Task.planned_start_at column. A positive result is cached for the process.</summary>
public sealed class EfTaskSchema(AppDbContext db) : ITaskSchema
{
    private static volatile bool ready;
    public async Task<bool> Ready(CancellationToken ct)
    {
        if (ready) return true;
        var value = await db.Database.SqlQueryRaw<int>("""
            SELECT CASE WHEN OBJECT_ID(N'dbo.Task',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.TaskAssignmentHistory',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.ShiftAssignment',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.StaffZoneAssignment',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.NotificationRecipient',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.ResponsePolicyAction',N'U') IS NOT NULL
                AND COL_LENGTH(N'dbo.Task',N'planned_start_at') IS NOT NULL
                THEN 1 ELSE 0 END AS Value
            """).SingleAsync(ct);
        ready = value == 1;
        return ready;
    }
}

/// <summary>Re-offers unaccepted tasks each retry interval, then hands them to the Operator (BR-22).</summary>
public sealed class TaskOfferWorker(IServiceScopeFactory scopes, IOptions<TaskOfferWorkerOptions> options, ILogger<TaskOfferWorker> log) : BackgroundService
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
                await scope.ServiceProvider.GetRequiredService<TaskDispatch>().Tick(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            // Log the type only: SQL messages can contain connection details.
            catch (Exception e) { log.LogWarning("Task offer retry failed ({ErrorType})", e.GetType().Name); }
        }
    }
}
