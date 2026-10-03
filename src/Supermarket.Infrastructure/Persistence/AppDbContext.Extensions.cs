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
        modelBuilder.Entity<MonitoringConfiguration>().Property(c => c.ConfidenceThreshold).ValueGeneratedNever();
    }
}
