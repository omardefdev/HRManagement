using System.Text.Json;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Services;

/// <summary>
/// PLAN-54 (§3): the cycle of a payroll framework version: four acts and at least three people. A (one or more editors) creates,
/// imports or edits the draft; B approves it; C attests it legally; B or C publishes it. Nobody does two of the first three, so an
/// editor never approves, attests or publishes. Each act of a v2 version passes the interim gate: every value has its legal
/// reference, and every interim value cites a STR-002 exception that is active that day. A refusal is an Arabic message and changes
/// nothing. These rules replace those of <see cref="PayrollLegalGovernance"/>, which keeps the attestation record and the change notice.
/// </summary>
public static class PayrollFrameworkGovernance
{
    public const string ApproverIsEditor = "لا يعتمد النسخة من أنشأها أو استوردها أو عدّلها: يعتمدها شخص آخر";
    public const string AttestorIsEditor = "لا يصادق قانونياً على النسخة من أنشأها أو استوردها أو عدّلها";
    public const string AttestorIsApprover = "لا يصادق قانونياً على النسخة من اعتمدها: يصادق عليها شخص ثالث";
    public const string PublisherIsEditor = "لا ينشر النسخة من أنشأها أو استوردها أو عدّلها";
    public const string PublisherNotApproverOrAttestor = "ينشر النسخة معتمدها أو المصادق عليها قانونياً";
    public const string LegacyDraft = "مسودة أقدم من حزم الإطار القانوني: أنشئ منها إصداراً جديداً وصنّف قيمه قبل الاعتماد";
    public const string UnknownActor = "تعذّر تحديد هوية المستخدم";

    /// <summary>The most findings a refusal lists; the count of the rest is added.</summary>
    public const int MaxFindingsListed = 5;

    public static bool IsEditor(PayrollConfiguration config, Guid actorId) => config.Editors.Any(e => e.UserId == actorId);

    /// <summary>The exceptions the version's interim values cite, each once, in order.</summary>
    public static IReadOnlyList<string> CitedExceptionIds(PayrollConfiguration config) =>
        config.IsLegacyFramework
            ? []
            : config.LegalValues()
                .Where(v => v.LegalStatus == PayrollLegalValueStatus.Interim && !string.IsNullOrWhiteSpace(v.InterimExceptionId))
                .Select(v => v.InterimExceptionId!.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

    /// <summary>The decisions the version's values cite (a legal reference that starts with <c>LD-</c>), each once, in order.</summary>
    public static IReadOnlyList<string> CitedDecisionIds(PayrollConfiguration config) =>
        config.LegalValues()
            .Select(v => LegalValueRules.CitedDecisionId(v.LegalReference))
            .Where(id => id is not null)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

    /// <summary>True when the version holds a value of status <c>Interim</c> (a v2 version only: a legacy one classifies nothing).</summary>
    public static bool HasInterimValues(PayrollConfiguration config) =>
        !config.IsLegacyFramework && config.LegalValues().Any(v => v.LegalStatus == PayrollLegalValueStatus.Interim);

    /// <summary>
    /// The interim gate: what stops an act on this version today. Empty for a legacy version (it cites nothing) and for a version
    /// whose every value has a reference and, when interim, an exception <paramref name="isExceptionActive"/> accepts today.
    /// Each finding names the value and the exception.
    /// </summary>
    public static IReadOnlyList<string> InterimGateFindings(PayrollConfiguration config, Func<string, bool> isExceptionActive)
    {
        if (config.IsLegacyFramework)
            return [];

        var findings = new List<string>();
        var known = config.FieldSources.Select(f => f.FieldName).ToHashSet(StringComparer.Ordinal);
        foreach (var field in PayrollConfigurationFieldSource.GovernedFields.Where(f => !known.Contains(f)))
            findings.Add($"الحقل {field}: بلا مرجع قانوني ولا حالة");

        foreach (var value in config.LegalValues())
        {
            if (string.IsNullOrWhiteSpace(value.LegalReference))
            {
                findings.Add($"{value.DescribeAr()}: بلا مرجع قانوني");
                continue;
            }

            if (value.LegalStatus != PayrollLegalValueStatus.Interim)
                continue;
            var exception = value.InterimExceptionId?.Trim();
            if (string.IsNullOrEmpty(exception))
                findings.Add($"{value.DescribeAr()}: قيمة مؤقتة لا تستشهد باستثناء");
            else if (!isExceptionActive(exception))
                findings.Add($"{value.DescribeAr()}: تستشهد بالاستثناء {exception} وهو غير نافذ اليوم في كتالوج الضوابط القانونية (غير موجود أو منتهٍ)");
        }

        return findings;
    }

    /// <summary>One Arabic message for a refused act: the first findings, and how many more there are.</summary>
    public static string DescribeFindings(string act, IReadOnlyList<string> findings)
    {
        var listed = string.Join("؛ ", findings.Take(MaxFindingsListed));
        var more = findings.Count > MaxFindingsListed
            ? FormattableString.Invariant($"؛ و{findings.Count - MaxFindingsListed} قيمة أخرى")
            : string.Empty;
        return $"لا {act} للنسخة: {listed}{more}";
    }

    /// <summary>The message of a calculation refused because an exception the pinned version relies on is no longer active.</summary>
    public static string ExceptionExpired(string exceptionId) => $"انتهى الاستثناء القانوني {exceptionId}";

    public static (bool Ok, string? Error) TryApprove(
        PayrollConfiguration config,
        Guid actorId,
        Func<string, bool> isExceptionActive,
        DateTime nowUtc)
    {
        if (actorId == Guid.Empty)
            return (false, UnknownActor);
        if (config.PublicationStatus != PayrollConfigPublicationStatus.Draft)
            return (false, PayrollFrameworkMessages.ApproveNeedsDraft);
        if (config.IsLegacyFramework)
            return (false, LegacyDraft);
        if (IsEditor(config, actorId))
            return (false, ApproverIsEditor);
        if (config.TaxBrackets.Count == 0)
            return (false, PayrollFrameworkMessages.NoBrackets);
        var findings = InterimGateFindings(config, isExceptionActive);
        if (findings.Count > 0)
            return (false, DescribeFindings("اعتماد", findings));

        // Approval fixes the content: the fingerprint is computed here and the version is immutable from now on.
        config.ContentHash = PayrollConfigurationHashService.ComputeContentHash(config);
        config.PublicationStatus = PayrollConfigPublicationStatus.Approved;
        config.ApprovedBy = actorId;
        config.ApprovedAt = nowUtc;
        return (true, null);
    }

    /// <summary>
    /// The legal attestation of an approved version by a third person. <paramref name="registeredDecisionIds"/> are the decisions the
    /// agency's register holds: every decision the version cites must be among them. The attestation records the fingerprint, the
    /// decisions and the exceptions it relies on.
    /// </summary>
    public static (bool Ok, string? Error, LegalAttestation? Attestation) TryAttest(
        PayrollConfiguration config,
        Guid actorId,
        Func<string, bool> isExceptionActive,
        IReadOnlyCollection<string> registeredDecisionIds,
        DateTime nowUtc)
    {
        if (actorId == Guid.Empty)
            return (false, UnknownActor, null);
        if (config.PublicationStatus != PayrollConfigPublicationStatus.Approved)
            return (false, PayrollFrameworkMessages.AttestNeedsApproved, null);
        if (string.IsNullOrEmpty(config.ContentHash))
            return (false, PayrollFrameworkMessages.NoFingerprint, null);
        if (IsEditor(config, actorId))
            return (false, AttestorIsEditor, null);
        if (config.ApprovedBy == actorId)
            return (false, AttestorIsApprover, null);
        var findings = InterimGateFindings(config, isExceptionActive);
        if (findings.Count > 0)
            return (false, DescribeFindings("تصديق", findings), null);

        var cited = CitedDecisionIds(config);
        var missing = cited.Where(id => !registeredDecisionIds.Contains(id)).ToList();
        if (missing.Count > 0)
            return (false, "لا تصديق للنسخة: تستشهد بقرارات ليست في سجل القرارات القانونية: " + string.Join("، ", missing.Take(MaxFindingsListed)), null);

        var attestation = PayrollLegalGovernance.CreateAttestation(config, actorId, config.IsLegacyFramework ? null : cited);
        attestation.AttestedAt = nowUtc;
        attestation.InterimExceptionIdsJson = JsonSerializer.Serialize(CitedExceptionIds(config));
        return (true, null, attestation);
    }

    public static (bool Ok, string? Error, PayrollConfigChangeNotice? Notice) TryPublish(
        PayrollConfiguration config,
        IList<PayrollConfiguration> sameCodeConfigs,
        Guid actorId,
        LegalAttestation? attestation,
        Func<string, bool> isExceptionActive,
        DateTime? latestPostedPeriodEnd,
        string? impactSummary = null)
    {
        if (actorId == Guid.Empty)
            return (false, UnknownActor, null);
        if (config.PublicationStatus != PayrollConfigPublicationStatus.Approved)
            return (false, PayrollFrameworkMessages.PublishNeedsApproved, null);
        if (IsEditor(config, actorId))
            return (false, PublisherIsEditor, null);

        var (attested, attestError) = PayrollLegalGovernance.EnsureAttestationForPublish(config, attestation);
        if (!attested)
            return (false, attestError, null);
        if (config.ApprovedBy != actorId && attestation!.AttestedBy != actorId)
            return (false, PublisherNotApproverOrAttestor, null);

        var findings = InterimGateFindings(config, isExceptionActive);
        if (findings.Count > 0)
            return (false, DescribeFindings("نشر", findings), null);

        var (published, error) = PayrollConfigurationVersioning.TryPublish(config, sameCodeConfigs, actorId, latestPostedPeriodEnd);
        if (!published)
            return (false, error, null);

        return (true, null, PayrollLegalGovernance.CreateChangeNotice(config, actorId, impactSummary));
    }
}
