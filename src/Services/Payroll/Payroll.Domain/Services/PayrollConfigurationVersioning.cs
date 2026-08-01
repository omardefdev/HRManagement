using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Services;

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

    public static PayrollConfiguration CloneAsDraft(
        PayrollConfiguration source,
        int nextVersionNumber,
        Action<PayrollConfiguration>? applyEdits = null)
    {
        if (source.PublicationStatus == PayrollConfigPublicationStatus.Draft
            && source.ContentHash is null)
        {
            // Editing an unpublished draft in place is allowed by callers; clone is for immutable rows.
        }

        var clone = new PayrollConfiguration
        {
            Code = source.Code,
            RegimeCode = source.RegimeCode,
            NameAr = source.NameAr,
            IsActive = false,
            VersionNumber = nextVersionNumber,
            PublicationStatus = PayrollConfigPublicationStatus.Draft,
            ContentHash = null,
            EffectiveFrom = source.EffectiveFrom,
            EffectiveTo = source.EffectiveTo,
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
                UpperLimit = b.UpperLimit,
                RatePercent = b.RatePercent,
                SortOrder = b.SortOrder,
                TenantId = source.TenantId
            });
        }

        applyEdits?.Invoke(clone);
        return clone;
    }

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
