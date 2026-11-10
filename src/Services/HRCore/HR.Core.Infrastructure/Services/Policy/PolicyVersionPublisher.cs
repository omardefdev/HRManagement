using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Policy;

/// <summary>PLAN-47: what a publication records besides the maker and the checker.</summary>
public sealed record PolicyPublicationDetails(
    Guid? MakerUserId,
    Guid? CheckerUserId,
    string? EvidenceRef,
    PolicyPublicationKind Kind,
    string? MakerRole,
    string? CheckerRole,
    Guid? RolledBackFromVersionId);

/// <summary>
/// Publishes draft policy versions: closes the prior EffectiveTo, records the publication and stages IPolicyPublished in the
/// same save (PLAN-02, PLAN-40). PLAN-47: called only by <see cref="PolicyPublishService"/>, which validates and checks four eyes.
/// </summary>
public interface IPolicyVersionPublisher
{
    Task<PolicyTemplateVersion> PublishDraftAsync(
        PolicyTemplateVersion version,
        Guid? makerUserId,
        Guid? checkerUserId,
        string? evidenceRef,
        PolicyPublicationDetails? details = null,
        CancellationToken ct = default);
}

public sealed class PolicyVersionPublisher(
    HRCoreDbContext db,
    IPolicyPublishedSnapshotCache snapshotCache,
    // PLAN-40: optional so manual constructions (tests) keep compiling; DI always supplies it.
    IPublishEndpoint? publish = null) : IPolicyVersionPublisher
{
    public async Task<PolicyTemplateVersion> PublishDraftAsync(
        PolicyTemplateVersion version,
        Guid? makerUserId,
        Guid? checkerUserId,
        string? evidenceRef,
        PolicyPublicationDetails? details = null,
        CancellationToken ct = default)
    {
        if (version.Status != PolicyVersionStatus.Draft)
            throw new InvalidOperationException("يمكن نشر المسودات فقط");

        var effectiveFrom = version.EffectiveFrom ?? DateTime.UtcNow;
        version.EffectiveFrom = effectiveFrom;
        version.Status = PolicyVersionStatus.Published;

        // A version effective later keeps the current one effective until then: only EffectiveTo of a published version changes.
        // PLAN-48: the version's own tenant, named (a provisioning session may not be that tenant's session).
        var previousPublished = await db.PolicyTemplateVersions.OwnOrNational(version.TenantId)
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
            TenantId = version.TenantId,
            Kind = details?.Kind ?? PolicyPublicationKind.Manual,
            MakerRole = details?.MakerRole,
            CheckerRole = details?.CheckerRole,
            RolledBackFromVersionId = details?.RolledBackFromVersionId
        });

        // PLAN-47: every publication touches its template, so two publications of one template race on its xmin and the loser
        // gets a concurrency conflict instead of a second "current" version.
        version.PolicyTemplate.UpdatedAt = DateTime.UtcNow;
        db.Entry(version.PolicyTemplate).Property(t => t.UpdatedAt).IsModified = true;

        // PLAN-40 (class A): staged before the save, so the publication and IPolicyPublished commit together on every path.
        // PLAN-47: the payload names the version fully, and every HR.Core instance clears its cache on it.
        if (publish is not null)
        {
            // Through the contract: the outbox delivers to the exchange of the published type only, and nothing binds the
            // concrete PolicyPublished exchange to IPolicyPublished, so a concrete publish was dropped by the broker.
            await publish.Publish<IPolicyPublished>(new
            {
                version.PolicyTemplate.Domain,
                VersionId = version.Id,
                PublishedAt = DateTime.UtcNow,
                TenantId = (Guid?)version.TenantId,
                version.VersionNumber,
                EffectiveFrom = (DateTime?)effectiveFrom
            }, p => p.MessageId = DeterministicMessageId.For<IPolicyPublished>(version.Id), ct);
        }

        await db.SaveChangesAsync(ct);
        snapshotCache.InvalidateDomain(version.PolicyTemplate.Domain);
        return version;
    }
}
