using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record OperationalEventTypeCreateRequest(string Code, string Name, string? Description, string MeasurementType, string Status = "INACTIVE");
public sealed record OperationalEventTypeUpdateRequest(string Name, string? Description, string MeasurementType, string Status);
public sealed record OperationalEventTypeAdminView(Guid EventTypeId, string Code, string Name, string? Description, string MeasurementType, string Status);

public sealed class OperationalEventTypeCatalog(ISetupStore store, ICurrentUser current)
{
    public async Task<OperationalEventTypeAdminView[]> List(CancellationToken ct)
    {
        UseCase.Admin(current);
        return (await store.List<OperationalEventType>(ct: ct)).OrderBy(x => x.Code, StringComparer.Ordinal)
            .Select(View).ToArray();
    }

    public async Task<OperationalEventTypeAdminView> Get(Guid id, CancellationToken ct)
    {
        UseCase.Admin(current);
        return View(UseCase.Found(await store.Find<OperationalEventType>(id, ct)));
    }

    public async Task<OperationalEventTypeAdminView> Create(OperationalEventTypeCreateRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        var code = Rules.Text(request.Code, 100, "Code").ToUpperInvariant();
        var measurement = Rules.Text(request.MeasurementType, 100, "Measurement type").ToUpperInvariant();
        Rules.Status(request.Status, "ACTIVE", "INACTIVE");
        ValidateRuntime(code, measurement, request.Status);
        UseCase.Unique((await store.List<OperationalEventType>(x => x.Code == code, ct)).Count > 0);
        Rules.Optional(request.Description, 1000, "Description");
        var entity = new OperationalEventType { EventTypeId = Guid.NewGuid(), Code = code,
            Name = Rules.Text(request.Name, 200, "Name"), Description = request.Description?.Trim(),
            MeasurementType = measurement, Status = request.Status };
        await store.Add(entity, ct);
        return View(entity);
    }

    public async Task<OperationalEventTypeAdminView> Update(Guid id, OperationalEventTypeUpdateRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        return await store.Transaction(async () =>
        {
            var entity = UseCase.Found(await store.Find<OperationalEventType>(id, ct));
            var measurement = Rules.Text(request.MeasurementType, 100, "Measurement type").ToUpperInvariant();
            Rules.Status(request.Status, "ACTIVE", "INACTIVE");
            ValidateRuntime(entity.Code, measurement, request.Status);
            var rules = await store.List<MonitoringRule>(x => x.EventTypeId == id, ct);
            Rules.Require(request.Status == "ACTIVE" || rules.All(x => !x.Enabled), "OPERATIONAL_EVENT_TYPE_IN_USE",
                "Disable monitoring rules before deactivating this event type.");
            Rules.Require(measurement == entity.MeasurementType || rules.Count == 0,
                "OPERATIONAL_EVENT_TYPE_IN_USE", "Remove monitoring rules before changing the measurement type.");
            entity.Name = Rules.Text(request.Name, 200, "Name");
            Rules.Optional(request.Description, 1000, "Description");
            entity.Description = request.Description?.Trim();
            entity.MeasurementType = measurement;
            entity.Status = request.Status;
            await store.Update(entity, ct);
            return View(entity);
        }, ct);
    }

    // Catalog entries can be referenced by rules, policies and historical events; never hard-delete them.
    public async Task Delete(Guid id, CancellationToken ct)
    {
        UseCase.Admin(current);
        var entity = UseCase.Found(await store.Find<OperationalEventType>(id, ct));
        await Update(id, new(entity.Name, entity.Description, entity.MeasurementType, "INACTIVE"), ct);
    }

    private static void ValidateRuntime(string code, string measurement, string status)
    {
        if (status != "ACTIVE") return;
        var supported = (code, measurement) switch
        {
            ("LONG_QUEUE", "QUEUE_LENGTH") => true,
            ("EXCESSIVE_WAIT", "WAITING_TIME") => true,
            ("CONGESTION", "PEOPLE_COUNT" or MonitoringPolicy.DensityMode) => true,
            ("CHECKOUT_CAPACITY", "CHECKOUT_CAPACITY") => true,
            _ => false
        };
        Rules.Require(supported, "RULE_RUNTIME_UNSUPPORTED", "This event type has no supported monitoring runtime; keep it inactive.");
    }

    private static OperationalEventTypeAdminView View(OperationalEventType entity) =>
        new(entity.EventTypeId, entity.Code, entity.Name, entity.Description, entity.MeasurementType, entity.Status);
}
