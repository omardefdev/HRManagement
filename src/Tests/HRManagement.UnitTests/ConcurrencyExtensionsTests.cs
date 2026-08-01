using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class ConcurrencyExtensionsTests
{
    [Fact]
    public void IsUniqueViolation_detects_23505_message()
    {
        var ex = new DbUpdateException("fail", new Exception("23505: duplicate key value violates unique constraint"));
        Assert.True(ConcurrencyExtensions.IsUniqueViolation(ex));
    }

    [Fact]
    public void IsUniqueViolation_false_for_other_errors()
    {
        var ex = new DbUpdateException("fail", new Exception("connection reset"));
        Assert.False(ConcurrencyExtensions.IsUniqueViolation(ex));
    }

    [Fact]
    public void IsConcurrencyConflict_detects_DbUpdateConcurrencyException()
    {
        var ex = new DbUpdateConcurrencyException("stale");
        Assert.True(ConcurrencyExtensions.IsConcurrencyConflict(ex));
    }

    [Fact]
    public void ConflictMessages_are_non_empty_arabic()
    {
        Assert.Contains("تعديل", ConcurrencyConflictMessages.StaleWrite);
        Assert.Contains("فريد", ConcurrencyConflictMessages.UniqueViolation);
        Assert.Contains("Idempotency", ConcurrencyConflictMessages.IdempotencyMismatch);
        Assert.Contains("رصيد", ConcurrencyConflictMessages.InsufficientBalance);
    }
}
