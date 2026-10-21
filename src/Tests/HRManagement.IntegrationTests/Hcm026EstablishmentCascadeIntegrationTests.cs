using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.MasterData;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-26 (AC-HCM-26h): freezing a funded position cascades to its open requisition on PostgreSQL.</summary>
[Trait("Category", "Testcontainers")]
public class Hcm026EstablishmentCascadeIntegrationTests(Hcm026PostgresFixture fixture) : IClassFixture<Hcm026PostgresFixture>
{
    [SkippableFact]
    public async Task Freeze_without_offer_cancels_published_requisition()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);

        var tenantId = Guid.NewGuid();
        await using var db = fixture.CreateContext(tenantId);
        var (position, requisition, application) = await SeedPublishedAsync(db, tenantId, offerStatus: null);
        var request = await AddFreezeRequestAsync(db, tenantId, position.Id, "إعادة هيكلة الوحدة");

        var result = await CreateApplier(db).ApplyAsync(request);

        Assert.True(result.Success, result.Error);
        await using var check = fixture.CreateContext(tenantId);
        Assert.Equal(FundedPositionStatus.Frozen, (await check.FundedPositions.SingleAsync(p => p.Id == position.Id)).Status);
        var req = await check.RecruitmentRequisitions.SingleAsync(r => r.Id == requisition.Id);
        Assert.Equal(RequisitionStatus.Cancelled, req.Status);
        Assert.Equal("تجميد المنصب: إعادة هيكلة الوحدة", req.CancellationReason);
        Assert.False((await check.PublicJobPostings.SingleAsync(p => p.RequisitionId == requisition.Id)).IsPublished);
        Assert.Equal(RecruitmentApplicationStatus.Rejected,
            (await check.RecruitmentApplications.SingleAsync(a => a.Id == application.Id)).Status);
        Assert.True(await check.AuditLogs.AnyAsync(l => l.EntityId == requisition.Id && l.Action == "CancelRequisition"));
    }

    [SkippableFact]
    public async Task Freeze_with_issued_offer_fails_and_changes_nothing()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);

        var tenantId = Guid.NewGuid();
        await using var db = fixture.CreateContext(tenantId);
        var (position, requisition, _) = await SeedPublishedAsync(db, tenantId, OfferStatus.Issued);
        var request = await AddFreezeRequestAsync(db, tenantId, position.Id, "إعادة هيكلة الوحدة");

        var result = await CreateApplier(db).ApplyAsync(request);

        Assert.False(result.Success);
        Assert.Equal(EstablishmentRecruitmentCascadeRules.FreezeBlockedByOfferMessage, result.Error);
        await using var check = fixture.CreateContext(tenantId);
        Assert.Equal(FundedPositionStatus.Vacant, (await check.FundedPositions.SingleAsync(p => p.Id == position.Id)).Status);
        Assert.Equal(RequisitionStatus.Published, (await check.RecruitmentRequisitions.SingleAsync(r => r.Id == requisition.Id)).Status);
    }

    private static async Task<(FundedPosition Position, RecruitmentRequisition Requisition, RecruitmentApplication Application)> SeedPublishedAsync(
        HRCoreDbContext db,
        Guid tenantId,
        OfferStatus? offerStatus)
    {
        var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenantId);
        var requisition = Hcm026PostgresFixture.NewRequisition(tenantId, position.Id, RequisitionStatus.Published);
        db.RecruitmentRequisitions.Add(requisition);
        var posting = new PublicJobPosting { TenantId = tenantId, Title = "محلل", IsPublished = true, RequisitionId = requisition.Id };
        db.PublicJobPostings.Add(posting);
        requisition.PublicJobPostingId = posting.Id;
        var application = new RecruitmentApplication
        {
            TenantId = tenantId,
            ApplicantFullName = "مرشح",
            AppliedPosition = "محلل",
            RequisitionId = requisition.Id,
            AppliedAt = DateTime.UtcNow,
            Status = offerStatus is null ? RecruitmentApplicationStatus.Shortlisted : RecruitmentApplicationStatus.OfferExtended,
            ConflictOfInterestDeclared = true
        };
        db.RecruitmentApplications.Add(application);
        if (offerStatus is OfferStatus status)
        {
            db.OfferLetters.Add(new OfferLetter
            {
                TenantId = tenantId,
                RecruitmentApplicationId = application.Id,
                FundedPositionId = position.Id,
                OfferNumber = $"OFF-{Guid.NewGuid():N}"[..14],
                Status = status,
                OfferedSalary = 1000m,
                ProposedStartDate = DateTime.UtcNow.AddDays(30)
            });
        }

        await db.SaveChangesAsync();
        return (position, requisition, application);
    }

    private static async Task<EstablishmentChangeRequest> AddFreezeRequestAsync(
        HRCoreDbContext db,
        Guid tenantId,
        Guid positionId,
        string reason)
    {
        var request = new EstablishmentChangeRequest
        {
            TenantId = tenantId,
            RequestNumber = $"ECR-{Guid.NewGuid():N}"[..14],
            ChangeType = EstablishmentChangeType.FreezeFundedPosition,
            Status = EstablishmentChangeStatus.Approved,
            TargetEntityId = positionId,
            PayloadJson = JsonSerializer.Serialize(new FreezeFundedPositionPayload { FundedPositionId = positionId, FreezeReason = reason }),
            RequestedAt = DateTime.UtcNow
        };
        db.EstablishmentChangeRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private static EstablishmentChangeApplier CreateApplier(HRCoreDbContext db) =>
        new(
            db,
            new EstablishmentService(),
            Mock.Of<IFundedPositionCodeService>(),
            Mock.Of<IJobCatalogCodeService>(),
            new EstablishmentReferenceValidator(new ReferenceDataProvider(db)),
            new JobCatalogVersionService(db),
            new FundedPositionVersionService(db));
}
