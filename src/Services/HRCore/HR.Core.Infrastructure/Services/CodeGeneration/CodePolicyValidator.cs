using HR.Core.Domain.Entities.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;

namespace HR.Core.Infrastructure.Services.CodeGeneration;

/// <summary>One thing wrong with a code policy, with an Arabic message.</summary>
public sealed record CodePolicyIssue(string Code, string MessageAr);

/// <summary>A code policy template as the default-template rule sees it.</summary>
public sealed record CodePolicyTemplateKey(Guid TenantId, CodeEntityKind Kind, CodePolicyScope Scope, string? PolicyKey);

/// <summary>
/// PLAN-48 (§3, G-COD-03): a code policy that cannot collide. Called when seeding, when an agency overlay is applied, when a
/// policy is upgraded and when a snapshot is resolved (an invalid published policy is refused, never used).
/// <list type="number">
/// <item>Exactly one Sequence segment, except a derived kind (<see cref="DerivedKinds"/>), where a Sequence segment is forbidden.</item>
/// <item>Every scope field of the sequence appears as a Field segment in the code.</item>
/// <item>A yearly reset needs a Date segment that shows the year (YYYY or YY).</item>
/// <item>A per-scope reset needs scope fields.</item>
/// <item>At most one default template (no policy key) per (tenant, kind) for agency overlays, and one national default per kind.</item>
/// </list>
/// </summary>
public static class CodePolicyValidator
{
    /// <summary>D-TEN-7: kinds whose code is derived from the facts, with no counter, and issued once.</summary>
    public static readonly IReadOnlySet<CodeEntityKind> DerivedKinds = new HashSet<CodeEntityKind> { CodeEntityKind.CostCenter };

    public static bool IsDerived(CodeEntityKind kind) => DerivedKinds.Contains(kind);

    public static IReadOnlyList<CodePolicyIssue> ValidateVersion(CodeEntityKind kind, string segmentsJson, CodeSequenceReset reset)
    {
        IReadOnlyList<HRManagement.SharedKernel.CodeGeneration.CodeSegmentDefinition> segments;
        try
        {
            segments = CodeFormat.ParseSegments(segmentsJson);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return [new CodePolicyIssue("SEGMENTS_JSON", $"مقاطع سياسة الترميز ليست JSON صالحاً: {ex.Message}")];
        }

        return ValidateVersion(kind, new CodeFormat(segments, reset));
    }

    public static IReadOnlyList<CodePolicyIssue> ValidateVersion(CodeEntityKind kind, CodeFormat format)
    {
        var issues = new List<CodePolicyIssue>();
        if (format.Segments.Count == 0)
            issues.Add(new("SEGMENTS_EMPTY", $"سياسة ترميز {kind} بلا مقاطع"));

        var sequences = format.Segments.Count(s => s.Kind == CodeSegmentKind.Sequence);
        if (IsDerived(kind))
        {
            if (sequences > 0)
                issues.Add(new("DERIVED_HAS_SEQUENCE", $"النوع {kind} مشتق من الحقائق بلا عدّاد، فلا مقطع تسلسل فيه"));
        }
        else if (sequences != 1)
        {
            issues.Add(new("SEQUENCE_COUNT", $"سياسة ترميز {kind} تتطلب مقطع تسلسل واحداً بالضبط، وفيها {sequences}"));
        }

        var shown = new HashSet<string>(format.FieldNames, StringComparer.OrdinalIgnoreCase);
        foreach (var field in format.ScopeFields.Where(f => !shown.Contains(f)))
            issues.Add(new("SCOPE_FIELD_NOT_SHOWN", $"حقل النطاق «{field}» لا يظهر في رقم {kind}، فيتكرر الرقم بين نطاقين"));

        if (format.Reset == CodeSequenceReset.Yearly
            && !format.Segments.Any(s => s.Kind == CodeSegmentKind.Date && s.Value is "YYYY" or "YY"))
            issues.Add(new("YEARLY_WITHOUT_YEAR", $"التصفير السنوي في {kind} يتطلب مقطع تاريخ بصيغة YYYY أو YY في الرقم"));

        if (format.Reset == CodeSequenceReset.PerScope && format.ScopeFields.Count == 0)
            issues.Add(new("PERSCOPE_WITHOUT_FIELDS", $"التصفير لكل نطاق في {kind} يتطلب حقول نطاق"));

        if (string.IsNullOrWhiteSpace(format.LeadingLiteral) && !format.Segments.Any(CodeFormat.IsFieldSegment))
            issues.Add(new("NO_PREFIX", $"رقم {kind} لا يبدأ ببادئة ولا بحقل"));

        return issues;
    }

    /// <summary>Rule 5: one default template per (tenant, kind) among the overlays, and one national default per kind.</summary>
    public static IReadOnlyList<CodePolicyIssue> ValidateDefaults(IEnumerable<CodePolicyTemplateKey> templates)
    {
        var issues = new List<CodePolicyIssue>();
        foreach (var group in templates
                     .Where(t => string.IsNullOrWhiteSpace(t.PolicyKey))
                     .GroupBy(t => (t.Scope, Tenant: t.Scope == CodePolicyScope.National ? Guid.Empty : t.TenantId, t.Kind))
                     .Where(g => g.Count() > 1))
        {
            issues.Add(group.Key.Scope == CodePolicyScope.National
                ? new("NATIONAL_DEFAULT_DUPLICATE", $"أكثر من قالب وطني افتراضي للنوع {group.Key.Kind}")
                : new("OVERLAY_DEFAULT_DUPLICATE", $"أكثر من طبقة افتراضية للنوع {group.Key.Kind} في الجهة {group.Key.Tenant}"));
        }

        return issues;
    }
}

/// <summary>PLAN-48: a published code policy that fails <see cref="CodePolicyValidator"/>; it is refused, never used (422).</summary>
public sealed class CodePolicyInvalidException(CodeEntityKind kind, string? policyKey, IReadOnlyList<CodePolicyIssue> issues)
    : InvalidOperationException($"The published code policy for {kind}{(policyKey is null ? "" : $" ({policyKey})")} is invalid: " +
                                string.Join("; ", issues.Select(i => i.Code)))
{
    public CodeEntityKind Kind { get; } = kind;

    public string? PolicyKey { get; } = policyKey;

    public IReadOnlyList<CodePolicyIssue> Issues { get; } = issues;

    public string MessageAr => $"سياسة الترميز المنشورة لـ {Kind} غير صالحة: " + string.Join("؛ ", Issues.Select(i => i.MessageAr));
}
