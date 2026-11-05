using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record IncidentTypeCreateRequest(string Code, string Name, string? Description, bool RequiresBeforePhoto, string Status = "ACTIVE");
public sealed record IncidentTypeUpdateRequest(string Name, string? Description, bool RequiresBeforePhoto, string Status);
public sealed record IncidentTypeAdminView(Guid IncidentTypeId, string Code, string Name, string? Description, bool RequiresBeforePhoto, string Status);

public sealed class IncidentTypeCatalog(ISetupStore store, ICurrentUser current)
{
    public async Task<IncidentTypeAdminView> Create(IncidentTypeCreateRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        var code = Rules.Text(request.Code, 50, "Code").Trim().ToUpperInvariant();
        Rules.Require(MonitoringPolicy.OperationalEventCode(code) is null,
            "AI_TYPE_CODE_RESERVED", "AI-detected IncidentTypes are frozen and cannot be created through this catalog.");
        Rules.Status(request.Status, "ACTIVE", "INACTIVE");
        UseCase.Unique((await store.List<IncidentType>(x => x.Code == code, ct)).Count > 0);
        Rules.Optional(request.Description, 1000, "Description");
        var entity = new IncidentType { IncidentTypeId = Guid.NewGuid(), Code = code, Name = Rules.Text(request.Name, 120, "Name"),
            Description = request.Description?.Trim(), RequiresBeforePhoto = request.RequiresBeforePhoto, Status = request.Status };
        await store.Add(entity, ct);
        return View(entity);
    }

    public async Task<IncidentTypeAdminView> Update(Guid id, IncidentTypeUpdateRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        Rules.Status(request.Status, "ACTIVE", "INACTIVE");
        var entity = UseCase.Found(await store.Find<IncidentType>(id, ct));
        entity.Name = Rules.Text(request.Name, 120, "Name");
        Rules.Optional(request.Description, 1000, "Description");
        entity.Description = request.Description?.Trim();
        entity.RequiresBeforePhoto = request.RequiresBeforePhoto;
        entity.Status = request.Status;
        await store.Update(entity, ct);
        return View(entity);
    }
    private static IncidentTypeAdminView View(IncidentType entity) => new(entity.IncidentTypeId, entity.Code, entity.Name,
        entity.Description, entity.RequiresBeforePhoto, entity.Status);
}
