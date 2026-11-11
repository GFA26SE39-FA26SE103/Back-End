using System.ComponentModel.DataAnnotations;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record ZoneAdjacencyReplaceRequest([Required] Guid[] AdjacentZoneIds);
public sealed record AdjacentZoneView(Guid ZoneId, Guid FloorId, string Code, string Name, string Status);

public sealed class ZoneAdjacencies(ISetupStore store, ICurrentUser current)
{
    public async Task<List<AdjacentZoneView>> List(Guid zoneId, CancellationToken ct)
    {
        UseCase.Viewer(current);
        var source = UseCase.Found(await store.Find<Zone>(zoneId, ct));
        var links = await store.List<ZoneAdjacency>(x => x.ZoneId == source.ZoneId, ct);
        return await Views(links.Select(x => x.AdjacentZoneId), ct);
    }

    public Task<List<AdjacentZoneView>> Replace(Guid zoneId, ZoneAdjacencyReplaceRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var source = UseCase.Found(await store.Find<Zone>(zoneId, ct));
            var ids = request.AdjacentZoneIds ?? [];
            if (ids.Contains(source.ZoneId))
                throw new ApplicationException("ZONE_ADJACENCY_SELF", "A zone cannot be adjacent to itself.", 422);
            if (ids.Distinct().Count() != ids.Length)
                throw new ApplicationException("ZONE_ADJACENCY_DUPLICATE", "Adjacent zone ids must be unique.", 422);

            var targets = new List<Zone>(ids.Length);
            foreach (var id in ids)
            {
                var target = UseCase.Found(await store.Find<Zone>(id, ct));
                if (target.FloorId != source.FloorId)
                    throw new ApplicationException("ZONE_ADJACENCY_CROSS_FLOOR", "Adjacent zones must belong to the same floor.", 422);
                targets.Add(target);
            }

            var currentLinks = await store.List<ZoneAdjacency>(
                x => x.ZoneId == source.ZoneId || x.AdjacentZoneId == source.ZoneId, ct);
            foreach (var link in currentLinks)
                await store.Remove(link, ct);

            foreach (var target in targets)
            {
                await store.Add(new ZoneAdjacency
                {
                    ZoneAdjacencyId = Guid.NewGuid(),
                    ZoneId = source.ZoneId,
                    AdjacentZoneId = target.ZoneId
                }, ct);
                await store.Add(new ZoneAdjacency
                {
                    ZoneAdjacencyId = Guid.NewGuid(),
                    ZoneId = target.ZoneId,
                    AdjacentZoneId = source.ZoneId
                }, ct);
            }

            return targets.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase).Select(View).ToList();
        }, ct);
    }

    private async Task<List<AdjacentZoneView>> Views(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var result = new List<AdjacentZoneView>();
        foreach (var id in ids.Distinct())
            result.Add(View(UseCase.Found(await store.Find<Zone>(id, ct))));
        return result.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static AdjacentZoneView View(Zone zone) =>
        new(zone.ZoneId, zone.FloorId, zone.Code, zone.Name, zone.Status);
}
