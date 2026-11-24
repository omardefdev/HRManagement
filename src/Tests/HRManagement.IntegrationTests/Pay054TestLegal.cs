using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payroll.Application.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-54: a clock a test moves. The interim gate reads the payroll calendar, so these tests never depend on today's date.</summary>
internal sealed class Pay054Clock(DateTimeOffset now) : TimeProvider
{
    /// <summary>23 November 2026, 10:00 UTC: both exceptions of the interim pack are active (they end on 31 January 2027).</summary>
    public static readonly DateTimeOffset Default = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Now { get; set; } = now;

    /// <summary>
    /// Runs once, at the next read of the clock: a handler reads the clock after it has read its rows and before it saves, so this is
    /// where a test lands another person's request in between.
    /// </summary>
    public Action? BeforeNextRead { get; set; }

    public override DateTimeOffset GetUtcNow()
    {
        var hook = BeforeNextRead;
        if (hook is not null)
        {
            BeforeNextRead = null;
            hook();
        }

        return Now;
    }
}

/// <summary>PLAN-54: the STR-002 catalog as a test holds it: each exception with its last day (the real catalog's two, by default).</summary>
internal sealed class Pay054CatalogGuard : IPayrollLegalControlGuard
{
    public const string Tax = "EX-STR2-TAX-INTERIM";
    public const string Law50 = "EX-STR2-LAW50-UNCERTIFIED";

    public Dictionary<string, DateOnly> LastDays { get; } = new(StringComparer.Ordinal)
    {
        [Tax] = new DateOnly(2027, 1, 31),
        [Law50] = new DateOnly(2027, 1, 31),
    };

    public string? ValidatePayrollRelease() => null;

    public bool IsCapabilityAllowed(string requirementId) => true;

    public bool IsExceptionActive(string? exceptionId, DateOnly asOf) => ExceptionExpiresOn(exceptionId) is { } lastDay && asOf <= lastDay;

    public DateOnly? ExceptionExpiresOn(string? exceptionId) =>
        exceptionId is not null && LastDays.TryGetValue(exceptionId.Trim(), out var lastDay) ? lastDay : null;
}

internal static class Pay054TestLegal
{
    /// <summary>A host or a scope with the test's clock and catalog (registered before the application's own, which are TryAdd).</summary>
    public static Action<IServiceCollection> Services(Pay054Clock clock, Pay054CatalogGuard? guard = null) => services =>
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(clock);
        if (guard is not null)
        {
            services.RemoveAll<IPayrollLegalControlGuard>();
            services.AddSingleton<IPayrollLegalControlGuard>(guard);
        }
    };
}
