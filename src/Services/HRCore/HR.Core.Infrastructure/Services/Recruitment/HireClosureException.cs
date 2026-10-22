using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>HCM-029 (A4): why a hire closure was refused.</summary>
public enum HireClosureErrorKind
{
    DuplicatePerson,
    Concurrency,
    PositionNotVacant,
    InvalidState,
    Legal
}

/// <summary>
/// HCM-029 (A4): expected hire-closure failure with an Arabic message for the form.
/// Derives from <see cref="InvalidOperationException"/> so existing callers that catch it keep working;
/// the validation throws that predate PLAN-29 stay plain <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class HireClosureException(HireClosureErrorKind kind, string messageAr, Exception? inner = null)
    : InvalidOperationException(messageAr, inner)
{
    public const string DuplicatePersonMessage = "الرقم الوطني مسجَّل لموظف قائم — استخدم «ربط موظف موجود».";
    public const string ConcurrencyMessage = "تغيّرت بيانات التعيين أثناء الحفظ (تعديل متزامن) — أعد تحميل الصفحة وحاول مجدداً.";
    public const string PositionNotVacantMessage = "المنصب لم يعد شاغراً.";

    public HireClosureErrorKind Kind { get; } = kind;

    /// <summary>
    /// Maps a save failure to a closure error: a unique violation (23505) on the employee national-id index is a
    /// duplicate person; any other unique violation or a concurrency conflict means a concurrent write. Null otherwise.
    /// </summary>
    public static HireClosureException? FromSaveFailure(DbUpdateException exception)
    {
        if (exception is DbUpdateConcurrencyException)
            return new HireClosureException(HireClosureErrorKind.Concurrency, ConcurrencyMessage, exception);

        if (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            return pg.ConstraintName is null || pg.ConstraintName.Contains("NationalIdHash", StringComparison.OrdinalIgnoreCase)
                ? new HireClosureException(HireClosureErrorKind.DuplicatePerson, DuplicatePersonMessage, exception)
                : new HireClosureException(HireClosureErrorKind.Concurrency, ConcurrencyMessage, exception);
        }

        return null;
    }
}
