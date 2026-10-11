using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Xunit;
using DbOperationalEvent = Supermarket.Infrastructure.Persistence.Scaffolded.OperationalEvent;

namespace Supermarket.Tests;

public sealed class OperationalEventModelTests
{
    [Fact]
    public void DatabaseV2EpisodeKeyAndColumnsAreMapped()
    {
        var options=new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=model-only;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        using var db=new AppDbContext(options);
        var entity=db.Model.FindEntityType(typeof(DbOperationalEvent))!;
        var table=StoreObjectIdentifier.Table("OperationalEvent",null);
        Assert.Equal("event_type_id",entity.FindProperty(nameof(DbOperationalEvent.EventTypeId))!.GetColumnName(table));
        Assert.Equal("cleared_at",entity.FindProperty(nameof(DbOperationalEvent.ClearedAt))!.GetColumnName(table));
        var index=entity.GetIndexes().Single(i=>i.GetDatabaseName()=="UX_OperationalEvent_Active_Camera_Zone_EventType");
        Assert.True(index.IsUnique); Assert.Equal("[cleared_at] IS NULL",index.GetFilter());
        Assert.Equal(new[] { "CameraId","ZoneId","EventTypeId" },index.Properties.Select(p=>p.Name));
        Assert.NotNull(db.Model.FindEntityType(typeof(OperationalEventType)));
    }
}
