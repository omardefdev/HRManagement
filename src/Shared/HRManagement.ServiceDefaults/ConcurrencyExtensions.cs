using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRManagement.ServiceDefaults;

/// <summary>DATA-001: optimistic concurrency via PostgreSQL xmin (skipped on SQLite/InMemory).</summary>
public static class ConcurrencyExtensions
{
    public static bool IsNpgsql(this DbContext context) =>
        context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Maps PostgreSQL <c>xmin</c> as a concurrency token when the active provider is Npgsql.
    /// Equivalent to Npgsql's UseXminAsConcurrencyToken without requiring a package-specific extension.
    /// </summary>
    public static EntityTypeBuilder<TEntity> ConfigureOptimisticConcurrency<TEntity>(
        this EntityTypeBuilder<TEntity> entity,
        DbContext context)
        where TEntity : class
    {
        if (!context.IsNpgsql())
            return entity;

        entity.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        return entity;
    }

    public static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            var type = ex.GetType();
            if (string.Equals(type.Name, "PostgresException", StringComparison.Ordinal))
            {
                var sqlState = type.GetProperty("SqlState")?.GetValue(ex) as string;
                if (sqlState == "23505")
                    return true;
            }

            if (ex.Message.Contains("23505", StringComparison.Ordinal)
                || ex.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsConcurrencyConflict(Exception exception)
    {
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is DbUpdateConcurrencyException)
                return true;
        }

        return false;
    }
}

/// <summary>User-facing Arabic conflict messages (DATA-001).</summary>
public static class ConcurrencyConflictMessages
{
    public const string StaleWrite =
        "تم تعديل السجل من مستخدم آخر. أعد التحميل ثم حاول مجدداً.";

    public const string UniqueViolation =
        "يتعارض الطلب مع قيد فريد — السجل موجود مسبقاً أو العملية مكررة.";

    public const string IdempotencyMismatch =
        "مفتاح Idempotency-Key مستخدم بجسم طلب مختلف.";

    public const string InsufficientBalance =
        "الرصيد أو الكمية غير كافية أو حدث تعارض تزامن.";
}
