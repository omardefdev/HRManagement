using System.Text.Json;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Services;

/// <summary>GOV-002: legal attest + change notice around PAY-002 publish.</summary>
public static class PayrollLegalGovernance
{
    public static LegalAttestation CreateAttestation(
        PayrollConfiguration config,
        Guid actorId,
        IEnumerable<string>? decisionIds = null)
    {
        var hash = config.ContentHash ?? PayrollConfigurationHashService.ComputeContentHash(config);
        config.ContentHash = hash;

        var ids = decisionIds?.ToList() ?? LegalDecisionIds.Law50AttestSet.ToList();
        if (string.Equals(config.RegimeCode, LegalRegimeCodes.CivilService2026, StringComparison.OrdinalIgnoreCase))
            ids = [LegalDecisionIds.CivilServicePending];

        return new LegalAttestation
        {
            Id = Guid.NewGuid(),
            PayrollConfigurationId = config.Id,
            AttestedBy = actorId,
            AttestedAt = DateTime.UtcNow,
            DecisionIdsJson = JsonSerializer.Serialize(ids),
            ContentHash = hash,
            Status = "Active",
            TenantId = config.TenantId
        };
    }

    public static (bool Ok, string? Error) EnsureAttestationForPublish(
        PayrollConfiguration config,
        LegalAttestation? attestation)
    {
        if (attestation is null || attestation.Status != "Active")
            return (false, "يلزم تصديق قانوني إلكتروني (LegalAttest) قبل النشر.");

        var hash = config.ContentHash ?? PayrollConfigurationHashService.ComputeContentHash(config);
        if (!string.Equals(attestation.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
            return (false, "التصديق القانوني لا يطابق بصمة المحتوى الحالية — أعد التصديق بعد التعديل.");

        if (attestation.PayrollConfigurationId != config.Id)
            return (false, "التصديق مرتبط بإصدار إعداد مختلف.");

        return (true, null);
    }

    public static PayrollConfigChangeNotice CreateChangeNotice(
        PayrollConfiguration config,
        Guid actorId,
        string? impactSummary = null)
    {
        var hash = config.ContentHash ?? string.Empty;
        var summary = string.IsNullOrWhiteSpace(impactSummary)
            ? $"نشر {config.Code} v{config.VersionNumber} ({config.RegimeCode}) — hash={hash[..Math.Min(16, hash.Length)]}…"
            : impactSummary.Trim();

        return new PayrollConfigChangeNotice
        {
            Id = Guid.NewGuid(),
            PayrollConfigurationId = config.Id,
            SupersedesConfigurationId = config.SupersedesConfigurationId,
            ContentHash = hash,
            ImpactSummary = summary,
            Channel = "in-app",
            PublishedAt = DateTime.UtcNow,
            PublishedBy = actorId,
            TenantId = config.TenantId
        };
    }

    public static (bool Ok, string? Error, PayrollConfigChangeNotice? Notice) TryPublishWithGovernance(
        PayrollConfiguration config,
        IList<PayrollConfiguration> sameCodeConfigs,
        Guid actorId,
        LegalAttestation? attestation,
        string? impactSummary = null,
        DateTime? effectiveFrom = null)
    {
        var (attestOk, attestErr) = EnsureAttestationForPublish(config, attestation);
        if (!attestOk)
            return (false, attestErr, null);

        var (ok, error) = PayrollConfigurationVersioning.TryPublish(config, sameCodeConfigs, actorId, effectiveFrom);
        if (!ok)
            return (false, error, null);

        var notice = CreateChangeNotice(config, actorId, impactSummary);
        return (true, null, notice);
    }
}
