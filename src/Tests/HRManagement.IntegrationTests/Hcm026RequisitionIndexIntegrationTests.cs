using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-26 (AC-HCM-26f): the filtered unique index on real PostgreSQL.</summary>
[Trait("Category", "Testcontainers")]
public class Hcm026RequisitionIndexIntegrationTests(Hcm026PostgresFixture fixture) : IClassFixture<Hcm026PostgresFixture>
{
    [SkippableFact]
    public async Task Duplicate_blocking_requisition_rejected_and_allowed_after_cancellation()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);

        var tenantId = Guid.NewGuid();
        await using var db = fixture.CreateContext(tenantId);
        var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenantId);
        var published = Hcm026PostgresFixture.NewRequisition(tenantId, position.Id, RequisitionStatus.Published);
        db.RecruitmentRequisitions.Add(published);
        await db.SaveChangesAsync();

        await using (var dup = fixture.CreateContext(tenantId))
        {
            dup.RecruitmentRequisitions.Add(Hcm026PostgresFixture.NewRequisition(tenantId, position.Id, RequisitionStatus.Draft));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
            var pg = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
            Assert.Equal("IX_RecruitmentRequisitions_Tenant_Position_Blocking", pg.ConstraintName);
        }

        var (cancelled, error) = await CreateLifecycle(db)
            .CancelAsync(published.Id, "إلغاء للاختبار التكاملي", Guid.NewGuid(), actorCanApprove: true);
        Assert.True(cancelled, error);

        await using (var after = fixture.CreateContext(tenantId))
        {
            after.RecruitmentRequisitions.Add(Hcm026PostgresFixture.NewRequisition(tenantId, position.Id, RequisitionStatus.Draft));
            await after.SaveChangesAsync();
            Assert.Equal(2, await after.RecruitmentRequisitions.CountAsync(r => r.FundedPositionId == position.Id));
        }
    }

    [SkippableFact]
    public async Task Non_blocking_statuses_do_not_conflict()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);

        var tenantId = Guid.NewGuid();
        await using var db = fixture.CreateContext(tenantId);
        var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenantId);
        db.RecruitmentRequisitions.Add(Hcm026PostgresFixture.NewRequisition(tenantId, position.Id, RequisitionStatus.Approved));
        db.RecruitmentRequisitions.Add(Hcm026PostgresFixture.NewRequisition(tenantId, position.Id, RequisitionStatus.Closed));
        db.RecruitmentRequisitions.Add(Hcm026PostgresFixture.NewRequisition(tenantId, position.Id, RequisitionStatus.Cancelled));
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.RecruitmentRequisitions.CountAsync(r => r.FundedPositionId == position.Id));
    }

    private static RecruitmentRequisitionLifecycleService CreateLifecycle(HRCoreDbContext db) =>
        new(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db)),
            Mock.Of<IWorkflowStarter>(),
            new ConfigurationBuilder().Build(),
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));
}
