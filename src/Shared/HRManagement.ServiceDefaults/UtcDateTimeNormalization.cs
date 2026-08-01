using HRManagement.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

public static class UtcDateTimeNormalization
{
    public static void Apply(DbContext context)
    {
        context.ChangeTracker.DetectChanges();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            foreach (var prop in entry.Properties)
            {
                var clrType = prop.Metadata.ClrType;
                if (clrType != typeof(DateTime) && clrType != typeof(DateTime?))
                    continue;

                if (prop.CurrentValue is not DateTime dt)
                    continue;

                var normalized = dt.Kind switch
                {
                    DateTimeKind.Utc => dt,
                    DateTimeKind.Local => dt.ToUniversalTime(),
                    _ => dt.AsUtc()
                };

                if (normalized.Kind == dt.Kind)
                    continue;

                prop.CurrentValue = normalized;

                // EF can treat DateTime values with equal ticks as unchanged even when their Kind differs.
                prop.Metadata.PropertyInfo?.SetValue(entry.Entity, normalized);
            }
        }
    }
}
