using Microsoft.EntityFrameworkCore;
namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class AppDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            if (type.FindProperty("UpdatedAt") is not null)
                modelBuilder.Entity(type.ClrType).Property<DateTime>("UpdatedAt").IsConcurrencyToken();
        }
        // Persist the requested enabled state explicitly; the current SQL default is false.
        modelBuilder.Entity<CameraConnection>().Property(c => c.IsEnabled).ValueGeneratedNever();
        // SQL DEFAULT 1 must not turn an explicitly disabled Draft rule back on.
        modelBuilder.Entity<MonitoringRule>().Property(r => r.Enabled).ValueGeneratedNever();
        // Zero is a valid explicit timing value, not a request for SQL's 30/300 defaults.
        modelBuilder.Entity<MonitoringRule>().Property(r => r.SustainSec).ValueGeneratedNever();
        modelBuilder.Entity<MonitoringRule>().Property(r => r.CooldownSec).ValueGeneratedNever();
        modelBuilder.Entity<MonitoringRule>().Property(r => r.ClearDurationSec).ValueGeneratedNever();
        modelBuilder.Entity<MonitoringConfiguration>().Property(c => c.ConfidenceThreshold).ValueGeneratedNever();

        // Reverse engineering sees the filtered unique "one ACCEPTED offer per task" index and
        // incorrectly models TaskOffer.TaskId as a one-to-one FK. The database permits many offers
        // per task and only filters uniqueness to status=ACCEPTED, so correct the runtime model here
        // without editing generated scaffold files.
        var offerType = modelBuilder.Entity<TaskOffer>().Metadata;
        var taskForeignKey = offerType.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(Task));
        offerType.RemoveForeignKey(taskForeignKey);
        modelBuilder.Entity<Task>().Ignore(t => t.TaskOffer);
        modelBuilder.Entity<TaskOffer>()
            .HasOne(o => o.Task)
            .WithMany()
            .HasForeignKey(o => o.TaskId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_TaskOffer_Task");

        // Filtered uniqueness limits only enabled queue sources, not the camera coverage
        // relation. A counter may have many enabled staff-observation camera mappings.
        var mappingType = modelBuilder.Entity<CameraCheckoutMapping>().Metadata;
        var counterForeignKey = mappingType.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(CheckoutCounter));
        mappingType.RemoveForeignKey(counterForeignKey);
        modelBuilder.Entity<CheckoutCounter>().Ignore(c => c.CameraCheckoutMapping);
        modelBuilder.Entity<CameraCheckoutMapping>()
            .HasOne(m => m.CheckoutCounter)
            .WithMany()
            .HasForeignKey(m => m.CheckoutCounterId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_CameraCheckoutMapping_Counter");

        // The standalone-policy filtered unique index allows historical/disabled
        // versions. Preserve its many-to-one FK after every database-first scaffold.
        var policyType = modelBuilder.Entity<Slapolicy>().Metadata;
        var taskTypeForeignKey = policyType.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(TaskType));
        policyType.RemoveForeignKey(taskTypeForeignKey);
        modelBuilder.Entity<TaskType>().Ignore(t => t.Slapolicy);
        modelBuilder.Entity<Slapolicy>()
            .HasOne(p => p.TaskType)
            .WithMany()
            .HasForeignKey(p => p.TaskTypeId)
            .HasConstraintName("FK_SLAPolicy_TaskType");
    }
}
