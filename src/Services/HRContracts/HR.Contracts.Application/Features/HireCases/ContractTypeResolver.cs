using System.Diagnostics.Metrics;
using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Application.Features.HireCases;

/// <summary>FUN-029: hire contract settings (<c>Contracts</c> section).</summary>
public sealed class HireContractOptions
{
    public const string SectionName = "Contracts";

    /// <summary>
    /// <c>Contracts:AllowDefaultContractTypeFallback</c> — when no ContractTypeRegimeMap matches, use the
    /// <c>DEFAULT</c> contract type (true in appsettings.json/Development; false in Production and Staging, where an
    /// unmatched hire opens an intervention instead).
    /// </summary>
    public bool AllowDefaultContractTypeFallback { get; set; } = true;
}

public enum ContractTypeMatch
{
    RegimeAndType,
    RegimeAnyType,
    AnyRegimeType,
    AnyRegimeAnyType,
    DefaultFallback,
    Unresolved
}

public sealed record ContractTypeResolution(ContractType? ContractType, ContractTypeMatch Match)
{
    public bool IsResolved => ContractType is not null;
}

/// <summary>
/// FUN-029 (F2): most specific active map wins — (regime, type), (regime, *), (*, type), (*, *) — then
/// <c>DEFAULT</c> only when <see cref="HireContractOptions.AllowDefaultContractTypeFallback"/> is on.
/// Regime ids and employment types compare case-insensitively.
/// </summary>
public sealed class ContractTypeResolver(IContractsRepository repository, HireContractOptions options)
{
    public const string DefaultContractTypeCode = "DEFAULT";

    public async Task<ContractTypeResolution> ResolveAsync(
        string? legalRegimeId,
        string? employmentType,
        CancellationToken ct = default)
    {
        var regime = Normalize(legalRegimeId);
        var type = Normalize(employmentType);

        var maps = await repository.ContractTypeRegimeMaps.AsNoTracking()
            .Include(m => m.ContractType)
            .Where(m => m.IsActive)
            .ToListAsync(ct);

        var best = maps
            .Select(m => (Map: m, Regime: Normalize(m.LegalRegimeId), Type: Normalize(m.EmploymentType)))
            .Where(x => (x.Regime is null || x.Regime == regime) && (x.Type is null || x.Type == type))
            .Where(x => x.Map.ContractType is not null)
            .OrderByDescending(x => (x.Regime is null ? 0 : 2) + (x.Type is null ? 0 : 1))
            .Select(x => (x.Map.ContractType, Match: MatchOf(x.Regime, x.Type)))
            .FirstOrDefault();

        if (best.ContractType is not null)
            return new ContractTypeResolution(best.ContractType, best.Match);

        if (options.AllowDefaultContractTypeFallback)
        {
            var fallback = await repository.ContractTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Code == DefaultContractTypeCode, ct);
            if (fallback is not null)
                return new ContractTypeResolution(fallback, ContractTypeMatch.DefaultFallback);
        }

        return new ContractTypeResolution(null, ContractTypeMatch.Unresolved);
    }

    private static ContractTypeMatch MatchOf(string? regime, string? type) => (regime, type) switch
    {
        (not null, not null) => ContractTypeMatch.RegimeAndType,
        (not null, null) => ContractTypeMatch.RegimeAnyType,
        (null, not null) => ContractTypeMatch.AnyRegimeType,
        _ => ContractTypeMatch.AnyRegimeAnyType
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == "*" ? null : value.Trim().ToLowerInvariant();
}

/// <summary>FUN-029: hire counters on the saga meter (<see cref="SagaMetrics.MeterName"/>, already exported).</summary>
public static class HireContractMetrics
{
    private static readonly Meter Meter = new(SagaMetrics.MeterName);

    /// <summary>Hire whose (regime, employment type) matched no contract type — intervention opened.</summary>
    public static readonly Counter<long> ContractTypeUnresolved = Meter.CreateCounter<long>("hire_contract_type_unresolved");

    /// <summary>Appointment flagged by the commencement watch.</summary>
    public static readonly Counter<long> CommencementOverdue = Meter.CreateCounter<long>("hire_commencement_overdue");
}
