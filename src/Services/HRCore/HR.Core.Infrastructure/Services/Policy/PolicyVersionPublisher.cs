using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>Publishes draft policy versions: closes prior EffectiveTo + cache invalidation (PLAN-02 / W2.2).</summary>
public interface IPolicyVersionPublisher
{
    Task<PolicyTemplateVersion> PublishDraftAsync(
        PolicyTemplateVersion version,
        Guid? makerUserId,
        Guid? checkerUserId,
        string? evidenceRef,
        CancellationToken ct = default);
}

public sealed class PolicyVersionPublisher(
    HRCoreDbContext db,
    IPolicyPublishedSnapshotCache snapshotCache) : IPolicyVersionPublisher
{
    public async Task<PolicyTemplateVersion> PublishDraftAsync(
        PolicyTemplateVersion version,
        Guid? makerUserId,
        Guid? checkerUserId,
        string? evidenceRef,
        CancellationToken ct = default)
    {
        if (version.Status != PolicyVersionStatus.Draft)
            throw new InvalidOperationException("يمكن نشر المسودات فقط");

        var effectiveFrom = version.EffectiveFrom ?? DateTime.UtcNow;
        version.EffectiveFrom = effectiveFrom;
        version.Status = PolicyVersionStatus.Published;

        var previousPublished = await db.PolicyTemplateVersions
            .Where(v => v.PolicyTemplateId == version.PolicyTemplateId
                        && v.Id != version.Id
                        && v.Status == PolicyVersionStatus.Published)
            .ToListAsync(ct);

        foreach (var prev in previousPublished)
        {
            if (prev.EffectiveTo is null || prev.EffectiveTo > effectiveFrom)
                prev.EffectiveTo = effectiveFrom;
        }

        db.PolicyPublications.Add(new PolicyPublication
        {
            PolicyTemplateVersionId = version.Id,
            MakerUserId = makerUserId,
            CheckerUserId = checkerUserId,
            EvidenceRef = evidenceRef,
            PublishedAt = DateTime.UtcNow,
            TenantId = version.TenantId
        });

        await db.SaveChangesAsync(ct);
        snapshotCache.InvalidateDomain(version.PolicyTemplate.Domain);
        return version;
    }
}
