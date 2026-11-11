using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;

namespace Supermarket.Infrastructure.Persistence;

public sealed class EfCheckoutEventHistory(AppDbContext db) : ICheckoutEventHistory
{
    public async Task<DateTime?> LatestClearedAt(Guid ruleId, Guid zoneId, CancellationToken ct)
    {
        var ended = await db.OperationalEvents.AsNoTracking().Where(e => e.RuleId == ruleId && e.ZoneId == zoneId && e.ClearedAt != null)
            .MaxAsync(e => e.ClearedAt, ct);
        return ended is null ? null : DateTime.SpecifyKind(ended.Value, DateTimeKind.Utc);
    }
}
