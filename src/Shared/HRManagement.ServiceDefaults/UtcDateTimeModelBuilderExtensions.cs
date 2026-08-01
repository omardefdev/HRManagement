using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HRManagement.ServiceDefaults;

public static class UtcDateTimeModelBuilderExtensions
{
    public static void ConfigureUtcDateTimeProperties(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(UtcDateTimeConverter.Instance);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(NullableUtcDateTimeConverter.Instance);
                }
            }
        }
    }

    private static DateTime ToUtcDateTime(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private sealed class UtcDateTimeConverter()
        : ValueConverter<DateTime, DateTime>(
            v => ToUtcDateTime(v),
            v => ToUtcDateTime(v))
    {
        public static readonly UtcDateTimeConverter Instance = new();
    }

    private sealed class NullableUtcDateTimeConverter()
        : ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? ToUtcDateTime(v.Value) : v,
            v => v.HasValue ? ToUtcDateTime(v.Value) : v)
    {
        public static readonly NullableUtcDateTimeConverter Instance = new();
    }
}
