using System.Globalization;
using HRManagement.SharedKernel.CodeGeneration;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

/// <summary>
/// PLAN-48 (§3): the one function that names a counter. The generator, the migration bootstrap (<see cref="CodSequenceBootstrapService"/>)
/// and the reseeding (<see cref="CodeCounterReseeder"/>) all build keys here, so they always meet on the same counter row.
/// <para>
/// Key: <c>tenant(N)|kind[|policyKey]|scope value...[|year]</c>. A non-empty policy key is part of the key, so a keyed template
/// (ASSET-PRIVILEGED) keeps its own counter beside the kind's default. Without a policy key the key is the one the generator built
/// before PLAN-48, so the counters of unchanged policies continue as they were.
/// </para>
/// </summary>
public static class CodeScopeKey
{
    public static string Build(
        Guid tenantId,
        CodeEntityKind kind,
        string? policyKey,
        IEnumerable<string> scopeValues,
        int? year)
    {
        var parts = new List<string> { tenantId.ToString("N"), kind.ToString() };
        if (!string.IsNullOrWhiteSpace(policyKey))
            parts.Add(policyKey.Trim());
        parts.AddRange(scopeValues.Select(v => v.Trim()));
        if (year is int y)
            parts.Add(y.ToString(CultureInfo.InvariantCulture));
        return string.Join("|", parts);
    }

    /// <summary>The counter's year column: the year for a yearly reset, 0 otherwise.</summary>
    public static int CounterYear(CodeSequenceReset reset, int year) => reset == CodeSequenceReset.Yearly ? year : 0;

    /// <summary>The year that is part of the key: only for a yearly reset.</summary>
    public static int? KeyYear(CodeSequenceReset reset, int year) => reset == CodeSequenceReset.Yearly ? year : null;
}
