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
    }
}
