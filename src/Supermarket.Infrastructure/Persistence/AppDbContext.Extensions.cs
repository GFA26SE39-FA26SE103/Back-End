using Microsoft.EntityFrameworkCore;
namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class AppDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OperationalEventType>(entity =>
        {
            entity.ToTable("OperationalEventType");
            entity.HasKey(e => e.EventTypeId).HasName("PK_OperationalEventType");
            entity.HasIndex(e => e.Code, "UQ_OperationalEventType_Code").IsUnique();
            entity.Property(e => e.EventTypeId).HasDefaultValueSql("(newsequentialid())", "DF_OperationalEventType_Id").HasColumnName("event_type_id");
            entity.Property(e => e.Code).HasMaxLength(100).HasColumnName("code");
            entity.Property(e => e.Name).HasMaxLength(200).HasColumnName("name");
            entity.Property(e => e.Description).HasMaxLength(1000).HasColumnName("description");
            entity.Property(e => e.MeasurementType).HasMaxLength(100).HasColumnName("measurement_type");
            entity.Property(e => e.Status).HasMaxLength(40).HasColumnName("status");
            entity.Property(e => e.CreatedAt).HasPrecision(3).HasDefaultValueSql("(sysutcdatetime())", "DF_OperationalEventType_CreatedAt").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasPrecision(3).HasDefaultValueSql("(sysutcdatetime())", "DF_OperationalEventType_UpdatedAt").HasColumnName("updated_at");
        });
        modelBuilder.Entity<MonitoringRule>(entity =>
        {
            entity.HasIndex(e => e.EventTypeId, "IX_MonitoringRule_EventTypeId");
            entity.HasIndex(e => new { e.ConfigId, e.EventTypeId }, "UQ_MonitoringRule_Config_EventType").IsUnique();
            entity.Property(e => e.EventTypeId).HasColumnName("event_type_id");
            entity.HasOne(e => e.EventType).WithMany(e => e.MonitoringRules)
                .HasForeignKey(e => e.EventTypeId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonitoringRule_OperationalEventType");
        });
        modelBuilder.Entity<OperationalEvent>(entity =>
        {
            entity.HasIndex(e => new { e.EventTypeId, e.DetectedAt }, "IX_OperationalEvent_EventType_Time").IsDescending(false, true);
            entity.HasIndex(e => new { e.CameraId, e.ZoneId, e.EventTypeId }, "UX_OperationalEvent_Active_Camera_Zone_EventType")
                .IsUnique().HasFilter("[cleared_at] IS NULL");
            entity.Property(e => e.EventTypeId).HasColumnName("event_type_id");
            entity.Property(e => e.Priority).HasMaxLength(40).HasColumnName("priority");
            entity.Property(e => e.LastObservedAt).HasPrecision(3).HasColumnName("last_observed_at");
            entity.Property(e => e.ClearedAt).HasPrecision(3).HasColumnName("cleared_at");
            entity.Property(e => e.UpdatedAt).HasPrecision(3).HasColumnName("updated_at");
            entity.HasOne(e => e.EventTypeNavigation).WithMany(e => e.OperationalEvents)
                .HasForeignKey(e => e.EventTypeId).OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OperationalEvent_OperationalEventType");
        });
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
        modelBuilder.Entity<MonitoringConfiguration>().Property(c => c.ConfidenceThreshold).ValueGeneratedNever();
    }
}
