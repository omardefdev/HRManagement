using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Services;

/// <summary>
/// PLAN-54: what a run of a framework version older than PLAN-54 reads from the general tables, handed to
/// <see cref="PayrollConfigurationVersioning.CloneAsDraft"/> so that a v2 draft of such a version starts from the same values.
/// </summary>
public sealed record PayrollLegacyReferenceData(
    IReadOnlyList<DeductionType> StatutoryDeductionTypes,
    IReadOnlyList<OfficialHoliday> Holidays,
    IReadOnlyList<DisbursementPriorityRule> ActivePriorityRules);

/// <summary>PAY-002: resolve / clone / approve / publish immutable payroll configuration versions.</summary>
public static class PayrollConfigurationVersioning
{
    /// <summary>
    /// The version that governs a day (a run's period start, or today): a version that was published — <c>Published</c> or, since
    /// PLAN-54, <c>Superseded</c> — whose dates cover the day. A superseded version still governs its own months: when version 2 is
    /// published from 1 January, December is still version 1's. Among several codes that cover the day, a code that has an active
    /// version comes first, then the latest effective date, then the highest version number; the code and the id settle a tie, so the
    /// answer never depends on the order the rows came in. <paramref name="regimeOrCode"/> keeps only the versions of one regime or
    /// one code.
    /// </summary>
    public static PayrollConfiguration? ResolveGoverning(
        IEnumerable<PayrollConfiguration> configs,
        DateTime day,
        string? regimeOrCode = null)
    {
        var date = NormalizeDate(day);
        var all = configs as IReadOnlyCollection<PayrollConfiguration> ?? configs.ToList();
        var activeCodes = all
            .Where(c => c.IsActive && c.PublicationStatus == PayrollConfigPublicationStatus.Published)
            .Select(c => c.Code)
            .ToHashSet(StringComparer.Ordinal);

        return all
            .Where(c => c.PublicationStatus is PayrollConfigPublicationStatus.Published or PayrollConfigPublicationStatus.Superseded)
            .Where(c => regimeOrCode is null || c.RegimeCode == regimeOrCode || c.Code == regimeOrCode)
            .Where(c => c.EffectiveFrom.Date <= date)
            .Where(c => c.EffectiveTo == null || c.EffectiveTo.Value.Date >= date)
            .OrderByDescending(c => activeCodes.Contains(c.Code))
            .ThenByDescending(c => c.EffectiveFrom)
            .ThenByDescending(c => c.VersionNumber)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .ThenBy(c => c.Id)
            .FirstOrDefault();
    }

    /// <summary>The version a run of the period starting on <paramref name="periodStart"/> is pinned to (see <see cref="ResolveGoverning"/>).</summary>
    public static PayrollConfiguration? ResolvePublishedForPeriod(
        IEnumerable<PayrollConfiguration> configs,
        DateTime periodStart) => ResolveGoverning(configs, periodStart);

    /// <summary>
    /// A new draft of <paramref name="source"/>'s code with every value that governs pay carried over: the numeric fields, the tax
    /// brackets, the statutory deductions, the holidays, the priority rules and the field sources, each with its legal reference,
    /// status and exception. The draft is always of fingerprint v2 and takes <paramref name="effectiveFrom"/> as its own date: a clone
    /// never inherits its source's dates (PLAN-54, P-6).
    /// <para>
    /// A source older than PLAN-54 (fingerprint v1) holds none of the three tables: its runs read the general tables. So the draft's
    /// starting rows are what such a run reads today, given in <paramref name="legacy"/>: the statutory deduction types, the general
    /// holidays (with the source's own) and the active general priority rules, each interim with no exception. The numbers do not
    /// move, and the draft cannot be approved before someone classifies those values (export, then import).
    /// </para>
    /// </summary>
    public static PayrollConfiguration CloneAsDraft(
        PayrollConfiguration source,
        int nextVersionNumber,
        DateTime effectiveFrom,
        Action<PayrollConfiguration>? applyEdits = null,
        PayrollLegacyReferenceData? legacy = null)
    {
        var clone = new PayrollConfiguration
        {
            Code = source.Code,
            RegimeCode = source.RegimeCode,
            NameAr = source.NameAr,
            IsActive = false,
            VersionNumber = nextVersionNumber,
            PublicationStatus = PayrollConfigPublicationStatus.Draft,
            HashSchemaVersion = PayrollConfiguration.CurrentHashSchemaVersion,
            ContentHash = null,
            EffectiveFrom = NormalizeDate(effectiveFrom),
            EffectiveTo = null,
            SupersedesConfigurationId = source.Id,
            ProrationDaysBasis = source.ProrationDaysBasis,
            DailyRateDivisor = source.DailyRateDivisor,
            MissionAllowanceAmount = source.MissionAllowanceAmount,
            SecondmentAllowancePercent = source.SecondmentAllowancePercent,
            EnableTrainingPremiums = source.EnableTrainingPremiums,
            TrainingPremiumAmount = source.TrainingPremiumAmount,
            MedicalCompensationDefaultAmount = source.MedicalCompensationDefaultAmount,
            MaxRetroCorrectionMonths = source.MaxRetroCorrectionMonths,
            EosGratuityRateFirstPeriod = source.EosGratuityRateFirstPeriod,
            EosGratuityFirstYearsThreshold = source.EosGratuityFirstYearsThreshold,
            EosGratuityRateAfterThreshold = source.EosGratuityRateAfterThreshold,
            NonWorkingDays = source.NonWorkingDays,
            UseDbHolidaysOnly = source.UseDbHolidaysOnly,
            TaxableBaseMode = source.TaxableBaseMode,
            TaxExemptionAmount = source.TaxExemptionAmount,
            LeavePayoutDailyDivisor = source.LeavePayoutDailyDivisor,
            RoundingMode = source.RoundingMode,
            DecimalPlaces = source.DecimalPlaces,
            CurrencyCode = source.CurrencyCode,
            TenantId = source.TenantId
        };

        foreach (var b in source.TaxBrackets.OrderBy(t => t.SortOrder))
        {
            clone.TaxBrackets.Add(new TaxBracket
            {
                PayrollConfigurationId = clone.Id,
                UpperLimit = b.UpperLimit,
                RatePercent = b.RatePercent,
                SortOrder = b.SortOrder,
                LegalReference = b.LegalReference,
                LegalStatus = b.LegalStatus,
                InterimExceptionId = b.InterimExceptionId,
                TenantId = source.TenantId
            });
        }

        if (source.IsLegacyFramework)
            AddLegacyStartingRows(clone, source, legacy);
        else
            CopyVersionTables(clone, source);

        applyEdits?.Invoke(clone);
        return clone;
    }

    /// <summary>A v2 source: its own tables and field sources, value for value.</summary>
    private static void CopyVersionTables(PayrollConfiguration clone, PayrollConfiguration source)
    {
        foreach (var d in source.StatutoryDeductions.OrderBy(x => x.SortOrder).ThenBy(x => x.Code, StringComparer.Ordinal))
        {
            clone.StatutoryDeductions.Add(new StatutoryDeductionRule
            {
                PayrollConfigurationId = clone.Id,
                Code = d.Code,
                NameAr = d.NameAr,
                EmployeePercent = d.EmployeePercent,
                EmployerPercent = d.EmployerPercent,
                FixedAmount = d.FixedAmount,
                BaseKind = d.BaseKind,
                Ceiling = d.Ceiling,
                InsuranceBody = d.InsuranceBody,
                LegalReference = d.LegalReference,
                LegalStatus = d.LegalStatus,
                InterimExceptionId = d.InterimExceptionId,
                SortOrder = d.SortOrder,
                TenantId = source.TenantId
            });
        }

        foreach (var h in source.Holidays.OrderBy(x => x.Date).ThenBy(x => x.NameAr, StringComparer.Ordinal))
            clone.Holidays.Add(CopyHoliday(h, clone, h.LegalReference, h.LegalStatus, h.InterimExceptionId));

        foreach (var r in source.PriorityRules.OrderBy(x => x.Priority).ThenBy(x => x.Category, StringComparer.Ordinal))
            clone.PriorityRules.Add(CopyPriorityRule(r, clone, r.LegalStatus, r.InterimExceptionId));

        foreach (var f in source.FieldSources.OrderBy(x => x.FieldName, StringComparer.Ordinal))
        {
            clone.FieldSources.Add(new PayrollConfigurationFieldSource
            {
                PayrollConfigurationId = clone.Id,
                FieldName = f.FieldName,
                LegalReference = f.LegalReference,
                LegalStatus = f.LegalStatus,
                InterimExceptionId = f.InterimExceptionId,
                TenantId = source.TenantId
            });
        }
    }

    /// <summary>A v1 source: what its runs read from the general tables becomes the draft's own, unclassified rows.</summary>
    private static void AddLegacyStartingRows(
        PayrollConfiguration clone,
        PayrollConfiguration source,
        PayrollLegacyReferenceData? legacy)
    {
        var order = 0;
        foreach (var type in (legacy?.StatutoryDeductionTypes ?? []).Where(t => t.IsStatutory))
        {
            clone.StatutoryDeductions.Add(new StatutoryDeductionRule
            {
                PayrollConfigurationId = clone.Id,
                Code = type.Code,
                NameAr = type.NameAr,
                EmployeePercent = type.Percentage ?? 0m,
                EmployerPercent = 0m,
                FixedAmount = type.FixedAmount ?? 0m,
                BaseKind = StatutoryDeductionBaseKind.GrossPay,
                Ceiling = null,
                InsuranceBody = null,
                LegalReference = null,
                LegalStatus = PayrollLegalValueStatus.Interim,
                InterimExceptionId = null,
                SortOrder = order++,
                TenantId = source.TenantId
            });
        }

        // A legacy run reads the general holidays and the version's own; the draft owns a copy of both.
        var holidays = (legacy?.Holidays ?? []).Concat(source.Holidays)
            .GroupBy(h => (h.Date, h.NameAr, h.IsRecurring))
            .Select(g => g.First())
            .OrderBy(h => h.Date)
            .ThenBy(h => h.NameAr, StringComparer.Ordinal);
        foreach (var h in holidays)
            clone.Holidays.Add(CopyHoliday(h, clone, null, PayrollLegalValueStatus.Interim, null));

        foreach (var r in (legacy?.ActivePriorityRules ?? [])
                     .Where(r => r.IsActive && r.PayrollConfigurationId is null)
                     .OrderBy(r => r.Priority)
                     .ThenBy(r => r.Category, StringComparer.Ordinal))
            clone.PriorityRules.Add(CopyPriorityRule(r, clone, PayrollLegalValueStatus.Interim, null));

        foreach (var field in PayrollConfigurationFieldSource.GovernedFields)
        {
            clone.FieldSources.Add(new PayrollConfigurationFieldSource
            {
                PayrollConfigurationId = clone.Id,
                FieldName = field,
                LegalReference = null,
                LegalStatus = PayrollLegalValueStatus.Interim,
                InterimExceptionId = null,
                TenantId = source.TenantId
            });
        }
    }

    private static OfficialHoliday CopyHoliday(
        OfficialHoliday source,
        PayrollConfiguration clone,
        string? legalReference,
        PayrollLegalValueStatus legalStatus,
        string? interimExceptionId) => new()
        {
            PayrollConfigurationId = clone.Id,
            Date = source.Date,
            NameAr = source.NameAr,
            IsRecurring = source.IsRecurring,
            LegalReference = legalReference,
            LegalStatus = legalStatus,
            InterimExceptionId = interimExceptionId,
            TenantId = clone.TenantId
        };

    private static DisbursementPriorityRule CopyPriorityRule(
        DisbursementPriorityRule source,
        PayrollConfiguration clone,
        PayrollLegalValueStatus legalStatus,
        string? interimExceptionId) => new()
        {
            // The version is the rule set of its own rules.
            RuleSetId = clone.Id,
            PayrollConfigurationId = clone.Id,
            Category = source.Category,
            Priority = source.Priority,
            MinimumAmount = source.MinimumAmount,
            LegalSource = source.LegalSource,
            // Never the general table's switch: a version's rule is in force through its version. Stored off, so that code older
            // than PLAN-54 (a rollback), which reads every active row for a decision that names no rule set, never mixes it in.
            IsActive = false,
            LegalStatus = legalStatus,
            InterimExceptionId = interimExceptionId,
            TenantId = clone.TenantId
        };

    /// <summary>A framework date is a day: midnight UTC, whatever kind the caller passed.</summary>
    public static DateTime NormalizeDate(DateTime value) =>
        DateTime.SpecifyKind(value.Kind == DateTimeKind.Local ? value.ToUniversalTime().Date : value.Date, DateTimeKind.Utc);

    public static (bool Success, string? Error) TryApprove(PayrollConfiguration config, Guid actorId)
    {
        if (config.PublicationStatus is not (PayrollConfigPublicationStatus.Draft
            or PayrollConfigPublicationStatus.Approved))
            return (false, "لا يمكن اعتماد إلا مسودة أو نسخة معتمدة سابقاً.");

        config.ContentHash = PayrollConfigurationHashService.ComputeContentHash(config);
        config.PublicationStatus = PayrollConfigPublicationStatus.Approved;
        config.ApprovedBy = actorId;
        config.ApprovedAt = DateTime.UtcNow;
        return (true, null);
    }

    /// <summary>
    /// Publishes an approved version from its own effective date (PLAN-54, P-6). Refused, with nothing changed, when the version is
    /// not approved (a draft is never published, and nobody is written in as its approver), has no brackets (the engine has no
    /// fallback for a published version), starts on or before the start of the code's published version (a publication never cuts
    /// back in time), or starts inside a period that already has a posted run of the same regime (D-PAY-13:
    /// <paramref name="latestPostedPeriodEnd"/> is the latest period end of those runs; a posted run pinned to no version counts for
    /// every regime). On success the code's published version is superseded and ends the day before, and only that.
    /// </summary>
    public static (bool Success, string? Error) TryPublish(
        PayrollConfiguration config,
        IList<PayrollConfiguration> sameCodeConfigs,
        Guid actorId,
        DateTime? latestPostedPeriodEnd = null)
    {
        if (config.PublicationStatus != PayrollConfigPublicationStatus.Approved)
            return (false, PayrollFrameworkMessages.PublishNeedsApproved);
        if (string.IsNullOrEmpty(config.ContentHash))
            return (false, PayrollFrameworkMessages.NoFingerprint);
        if (config.TaxBrackets.Count == 0)
            return (false, PayrollFrameworkMessages.NoBrackets);

        var from = NormalizeDate(config.EffectiveFrom);
        var predecessors = sameCodeConfigs
            .Where(c => c.Id != config.Id
                        && c.Code == config.Code
                        && c.PublicationStatus == PayrollConfigPublicationStatus.Published)
            .ToList();

        if (predecessors.Any(p => from <= NormalizeDate(p.EffectiveFrom)))
            return (false, PayrollFrameworkMessages.NotAfterPublished(predecessors.Max(p => NormalizeDate(p.EffectiveFrom))));
        if (latestPostedPeriodEnd is { } posted && NormalizeDate(posted) >= from)
            return (false, PayrollFrameworkMessages.InsidePostedPeriod(from, NormalizeDate(posted)));

        foreach (var other in predecessors)
        {
            other.PublicationStatus = PayrollConfigPublicationStatus.Superseded;
            other.IsActive = false;
            other.EffectiveTo = from.AddDays(-1);
        }

        config.PublicationStatus = PayrollConfigPublicationStatus.Published;
        config.PublishedBy = actorId;
        config.PublishedAt = DateTime.UtcNow;
        config.EffectiveFrom = from;
        config.IsActive = true;
        return (true, null);
    }

    /// <summary>
    /// Makes a published version the active one of its code. It never publishes: any other status is refused (plan §3, rule 3).
    /// </summary>
    public static (bool Success, string? Error) TryActivate(PayrollConfiguration config, IEnumerable<PayrollConfiguration> sameCodeConfigs)
    {
        if (config.PublicationStatus != PayrollConfigPublicationStatus.Published)
            return (false, PayrollFrameworkMessages.ActivateNeedsPublished);

        foreach (var other in sameCodeConfigs.Where(c => c.Code == config.Code))
            other.IsActive = other.Id == config.Id;
        config.IsActive = true;
        return (true, null);
    }

    public static string? GuardImmutableMutation(PayrollConfiguration config) =>
        config.IsImmutable
            ? "لا يُسمح بتعديل نسخة معتمدة أو منشورة؛ أنشئ إصداراً جديداً."
            : null;
}
