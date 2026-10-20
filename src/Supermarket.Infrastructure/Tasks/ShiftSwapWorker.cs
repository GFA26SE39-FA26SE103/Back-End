using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Task = System.Threading.Tasks.Task;
namespace Supermarket.Infrastructure.Tasks;

public sealed class ShiftSwapWorkerOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>How often pending SWAP/COVER requests are checked for their 24-hour deadline.</summary>
    public int PollSeconds { get; set; } = 60;
}

/// <summary>Detects the ERD V6 SWAP/COVER structures (V4 ShiftSwapRequest plus V5 expiry/cancellation columns). A positive result is cached for the process.</summary>
public sealed class EfShiftSwapSchema(AppDbContext db) : IShiftSwapSchema
{
    private static volatile bool ready;
    public async Task<bool> Ready(CancellationToken ct)
    {
        if (ready) return true;
        var value = await db.Database.SqlQueryRaw<int>("""
            SELECT CASE WHEN OBJECT_ID(N'dbo.ShiftSwapRequest',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.ShiftAssignment',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.StaffZoneAssignment',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.StaffUnavailability',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.NotificationRecipient',N'U') IS NOT NULL
                AND COL_LENGTH(N'dbo.ShiftSwapRequest',N'expires_at') IS NOT NULL
                AND COL_LENGTH(N'dbo.ShiftSwapRequest',N'cancelled_by_user_id') IS NOT NULL
                AND COL_LENGTH(N'dbo.Zone',N'min_staff') IS NOT NULL
                THEN 1 ELSE 0 END AS Value
            """).SingleAsync(ct);
        ready = value == 1;
        return ready;
    }
}

/// <summary>Expires pending SWAP/COVER requests 24 hours before the earliest affected shift.</summary>
public sealed class ShiftSwapWorker(IServiceScopeFactory scopes, IOptions<ShiftSwapWorkerOptions> options, ILogger<ShiftSwapWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollSeconds, 5, 3600)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ShiftSwaps>().ExpireDue(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            // Log the type only: SQL messages can contain connection details.
            catch (Exception e) { log.LogWarning("Shift swap expiry failed ({ErrorType})", e.GetType().Name); }
        }
    }
}
