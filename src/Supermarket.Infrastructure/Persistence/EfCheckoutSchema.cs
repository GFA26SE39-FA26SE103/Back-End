using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;

namespace Supermarket.Infrastructure.Persistence;

/// <summary>Read-only metadata probe; no checkout entity query/reflection or process-global readiness cache.</summary>
public sealed class EfCheckoutSchema(AppDbContext db) : ICheckoutSchema
{
    public async Task<bool> Ready(CancellationToken ct)
    {
        const string counter = "dbo.CheckoutCounter";
        const string mapping = "dbo.CameraCheckoutMapping";
        try
        {
            var value = await db.Database.SqlQuery<int>($"""
                SELECT CASE WHEN OBJECT_ID({counter}, N'U') IS NOT NULL
                    AND OBJECT_ID({mapping}, N'U') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM (VALUES
                            (N'checkout_counter_id'), (N'zone_id'), (N'code'), (N'name'),
                            (N'is_enabled'), (N'created_at'), (N'updated_at')
                        ) AS required_columns(column_name)
                        WHERE COL_LENGTH({counter}, required_columns.column_name) IS NULL)
                    AND NOT EXISTS (
                        SELECT 1 FROM (VALUES
                            (N'mapping_id'), (N'camera_id'), (N'checkout_counter_id'),
                            (N'lane_roi'), (N'staff_roi'), (N'service_roi'), (N'is_enabled'), (N'is_queue_source'),
                            (N'created_at'), (N'updated_at')
                        ) AS required_columns(column_name)
                        WHERE COL_LENGTH({mapping}, required_columns.column_name) IS NULL)
                    AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID({counter})
                        AND name=N'UQ_CheckoutCounter_Zone_Code' AND is_unique=1 AND is_disabled=0)
                    AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID({mapping})
                        AND name=N'UQ_CameraCheckoutMapping_Camera_Counter' AND is_unique=1 AND is_disabled=0)
                    AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID({mapping})
                        AND name=N'UX_CameraCheckoutMapping_EnabledQueueSource' AND is_unique=1 AND is_disabled=0 AND has_filter=1)
                    AND (SELECT COUNT(*) FROM sys.foreign_keys WHERE name IN
                        (N'FK_CheckoutCounter_Zone',N'FK_CameraCheckoutMapping_Camera',N'FK_CameraCheckoutMapping_Counter')
                        AND parent_object_id IN (OBJECT_ID({counter}),OBJECT_ID({mapping}))
                        AND is_disabled=0 AND is_not_trusted=0)=3
                    THEN 1 ELSE 0 END AS Value
                """).SingleAsync(ct);
            return value == 1;
        }
        catch (DbException) { return false; }
        catch (TimeoutException) { return false; }
    }
}
