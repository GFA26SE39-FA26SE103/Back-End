using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Xunit;

namespace Supermarket.Tests;

public sealed class CheckoutPersistenceMappingTests
{
    private static AppDbContext ModelOnly() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=localhost;Database=model-only;Integrated Security=true;TrustServerCertificate=true").Options);

    [Fact]
    public void CheckoutHasTwoRequiredPolygonsAndOnlyQueueSourcesAreUniquePerCounter()
    {
        using var db = ModelOnly();
        var mapping = db.Model.FindEntityType(typeof(CameraCheckoutMapping))!;
        var table = StoreObjectIdentifier.Table("CameraCheckoutMapping", null);
        Assert.Equal("mapping_id", mapping.FindProperty(nameof(CameraCheckoutMapping.MappingId))!.GetColumnName(table));
        Assert.False(mapping.FindProperty(nameof(CameraCheckoutMapping.LaneRoi))!.IsNullable);
        Assert.False(mapping.FindProperty(nameof(CameraCheckoutMapping.StaffRoi))!.IsNullable);
        Assert.True(mapping.FindProperty(nameof(CameraCheckoutMapping.UpdatedAt))!.IsConcurrencyToken);
        Assert.False(mapping.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(CheckoutCounter)).IsUnique);
        var source = mapping.GetIndexes().Single(i => i.GetDatabaseName() == "UX_CameraCheckoutMapping_EnabledQueueSource");
        Assert.True(source.IsUnique);
        Assert.Contains("[is_enabled]=(1)", source.GetFilter());
        Assert.Contains("[is_queue_source]=(1)", source.GetFilter());
        var pair = mapping.GetIndexes().Single(i => i.GetDatabaseName() == "UQ_CameraCheckoutMapping_Camera_Counter");
        Assert.True(pair.IsUnique);
        Assert.Equal(new[] { "CameraId", "CheckoutCounterId" }, pair.Properties.Select(p => p.Name));
        var counter = db.Model.FindEntityType(typeof(CheckoutCounter))!;
        Assert.Equal("CheckoutCounter", counter.GetTableName());
        Assert.Equal(50, counter.FindProperty(nameof(CheckoutCounter.Code))!.GetMaxLength());
        Assert.True(counter.FindProperty(nameof(CheckoutCounter.UpdatedAt))!.IsConcurrencyToken);
        Assert.True(counter.GetIndexes().Single(i => i.GetDatabaseName() == "UQ_CheckoutCounter_Zone_Code").IsUnique);
    }

    [Fact]
    public void RescaffoldingDoesNotTurnVersionedStandalonePoliciesIntoOneToOne()
    {
        using var db = ModelOnly();
        var policy = db.Model.FindEntityType(typeof(Slapolicy))!;
        Assert.False(policy.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(TaskType)).IsUnique);
        var index = policy.GetIndexes().Single(i => i.GetDatabaseName() == "UX_SLAPolicy_Enabled_StandaloneTaskType");
        Assert.True(index.IsUnique);
        Assert.Contains("[is_enabled]=(1)", index.GetFilter());
        Assert.Contains("STANDALONE_TASK", index.GetFilter());
    }
}
