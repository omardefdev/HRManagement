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
    public static PayrollConfiguration? ResolvePublishedForPeriod(
        IEnumerable<PayrollConfiguration> configs,
        DateTime periodStart)
    {
        var start = periodStart.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(periodStart.Date, DateTimeKind.Utc)
            : periodStart.ToUniversalTime().Date;

        return configs
            .Where(c => c.PublicationStatus == PayrollConfigPublicationStatus.Published)
            .Where(c => c.EffectiveFrom.Date <= start)
            .Where(c => c.EffectiveTo == null || c.EffectiveTo.Value.Date >= start)
            .OrderByDescending(c => c.IsActive)
            .ThenByDescending(c => c.EffectiveFrom)
            .ThenByDescending(c => c.VersionNumber)
            .FirstOrDefault();
    }

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
            IsActive = true,
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

    public static (bool Success, string? Error) TryPublish(
        PayrollConfiguration config,
        IList<PayrollConfiguration> sameCodeConfigs,
        Guid actorId,
        DateTime? effectiveFrom = null)
    {
        if (config.PublicationStatus is not (PayrollConfigPublicationStatus.Approved
            or PayrollConfigPublicationStatus.Draft))
            return (false, "لا يمكن نشر إلا مسودة أو نسخة معتمدة.");

        config.ContentHash ??= PayrollConfigurationHashService.ComputeContentHash(config);
        if (config.PublicationStatus == PayrollConfigPublicationStatus.Draft)
        {
            config.ApprovedBy ??= actorId;
            config.ApprovedAt ??= DateTime.UtcNow;
        }

        var from = (effectiveFrom ?? config.EffectiveFrom).Date;
        if (from.Kind == DateTimeKind.Unspecified)
            from = DateTime.SpecifyKind(from, DateTimeKind.Utc);

        foreach (var other in sameCodeConfigs.Where(c =>
                     c.Id != config.Id
                     && c.Code == config.Code
                     && c.PublicationStatus == PayrollConfigPublicationStatus.Published))
        {
            other.PublicationStatus = PayrollConfigPublicationStatus.Superseded;
            other.IsActive = false;
            if (other.EffectiveTo == null || other.EffectiveTo >= from)
                other.EffectiveTo = from.AddDays(-1);
        }

        config.PublicationStatus = PayrollConfigPublicationStatus.Published;
        config.PublishedBy = actorId;
        config.PublishedAt = DateTime.UtcNow;
        config.EffectiveFrom = from;
        config.IsActive = true;
        return (true, null);
    }

    public static string? GuardImmutableMutation(PayrollConfiguration config) =>
        config.IsImmutable
            ? "لا يُسمح بتعديل نسخة معتمدة أو منشورة؛ أنشئ إصداراً جديداً."
            : null;
}
