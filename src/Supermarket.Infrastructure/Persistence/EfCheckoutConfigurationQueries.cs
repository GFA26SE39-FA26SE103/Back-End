using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Domain;
using Db = Supermarket.Infrastructure.Persistence.Scaffolded;

namespace Supermarket.Infrastructure.Persistence;

/// <summary>Read-only, scoped queries. Callers must check ICheckoutSchema before querying checkout tables.</summary>
public sealed class EfCheckoutConfigurationQueries(Db.AppDbContext db) : ICheckoutConfigurationQueries
{
    internal IQueryable<CheckoutCounter> CountersByZoneQuery(Guid zoneId, int limit)
        => db.CheckoutCounters.AsNoTracking().Where(c => c.ZoneId == zoneId)
            .OrderBy(c => c.Code).ThenBy(c => c.CheckoutCounterId).Take(SentinelLimit(limit))
            .Select(c => new CheckoutCounter { CheckoutCounterId = c.CheckoutCounterId, ZoneId = c.ZoneId,
                Code = c.Code, Name = c.Name, IsEnabled = c.IsEnabled, CreatedAt = c.CreatedAt, UpdatedAt = c.UpdatedAt });

    internal IQueryable<CameraCheckoutMapping> MappingsByCameraQuery(Guid cameraId, int limit)
        => Project(db.CameraCheckoutMappings.AsNoTracking().Where(m => m.CameraId == cameraId)
            .OrderBy(m => m.CheckoutCounterId).ThenBy(m => m.MappingId).Take(SentinelLimit(limit)));

    internal IQueryable<CameraCheckoutMapping> MappingsByCounterQuery(Guid counterId, int limit)
        => Project(db.CameraCheckoutMappings.AsNoTracking().Where(m => m.CheckoutCounterId == counterId)
            .OrderBy(m => m.CameraId).ThenBy(m => m.MappingId).Take(SentinelLimit(limit)));

    public async Task<List<CheckoutCounter>> CountersByZone(Guid zoneId, int limit, CancellationToken ct)
        => Utc(await CountersByZoneQuery(zoneId, limit).ToListAsync(ct));

    public async Task<List<CameraCheckoutMapping>> MappingsByCamera(Guid cameraId, int limit, CancellationToken ct)
        => Utc(await MappingsByCameraQuery(cameraId, limit).ToListAsync(ct));

    public async Task<List<CameraCheckoutMapping>> MappingsByCounter(Guid counterId, int limit, CancellationToken ct)
        => Utc(await MappingsByCounterQuery(counterId, limit).ToListAsync(ct));

    private static IQueryable<CameraCheckoutMapping> Project(IQueryable<Db.CameraCheckoutMapping> rows)
        => rows.Select(m => new CameraCheckoutMapping { MappingId = m.MappingId, CameraId = m.CameraId,
            CheckoutCounterId = m.CheckoutCounterId, LaneRoi = m.LaneRoi, StaffRoi = m.StaffRoi,
            IsEnabled = m.IsEnabled, IsQueueSource = m.IsQueueSource, CreatedAt = m.CreatedAt, UpdatedAt = m.UpdatedAt });

    private static int SentinelLimit(int limit)
    {
        if (limit is <= 0 or int.MaxValue) throw new ArgumentOutOfRangeException(nameof(limit));
        return limit + 1;
    }

    private static List<T> Utc<T>(List<T> rows) where T : TrackedEntity
    {
        // Only limit + 1 rows, with no navigation/global scans and no tracked scaffold entities.
        foreach (var row in rows)
        {
            row.CreatedAt = DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc);
            row.UpdatedAt = DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc);
        }
        return rows;
    }
}
