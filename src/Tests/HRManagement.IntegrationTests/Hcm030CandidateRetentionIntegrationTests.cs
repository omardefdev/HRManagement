using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.IntegrationTests;

/// <summary>
/// HCM-030 (G3): the candidate retention job on a migrated PostgreSQL — more than one batch of pairs (100) in one tenant
/// plus a second tenant, run across tenants under the job's advisory lock: every due pair is anonymized in its own
/// transaction, its documents released (tenant-stamped outbox rows) and deleted, held and open pairs are untouched, one
/// run row is logged per tenant, and a second run finds nothing due.
/// </summary>
[Trait("Category", "Testcontainers")]
public class Hcm030CandidateRetentionIntegrationTests(Hcm026PostgresFixture fixture) : IClassFixture<Hcm026PostgresFixture>
{
    [SkippableFact]
    public async Task Retention_runs_on_a_real_database_in_batches_across_tenants()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var now = DateTime.UtcNow;
        var closedAt = now.AddMonths(-40);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var seedA = await SeedAsync(tenantA, duePairs: 110, closedAt);
        var seedB = await SeedAsync(tenantB, duePairs: 15, closedAt);

        // The class has its own database, but other tenants' rows must not matter: count only what the run reports per tenant.
        IReadOnlyList<CandidateRetentionRun>? runs = null;
        await using (var db = fixture.CreateContext(Guid.Empty))
        {
            var acquired = await AdvisoryLeaderElection.TryRunAsLeaderAsync(
                db,
                CandidateRetentionJob.AdvisoryLockKey,
                async ct => runs = await Service(db).RunAsync(
                    new CandidateRetentionRunRequest(now, Apply: true, CandidateRetentionRun.JobTrigger), ct));
            Assert.True(acquired);
        }

        Assert.NotNull(runs);
        var runA = runs!.Single(r => r.TenantId == tenantA);
        var runB = runs!.Single(r => r.TenantId == tenantB);
        Assert.False(runA.DryRun);
        Assert.Equal(110, runA.DueCount);
        Assert.Equal(110, runA.AnonymizedCount);
        Assert.Equal(1, runA.SkippedLegalHold);
        Assert.Null(runA.Error);
        Assert.Equal(15, runB.DueCount);
        Assert.Equal(15, runB.AnonymizedCount);

        await AssertTenantAsync(seedA);
        await AssertTenantAsync(seedB);

        // A second run (dry or not) finds nothing left to do.
        await using (var db = fixture.CreateContext(Guid.Empty))
        {
            var again = await Service(db).RunAsync(new CandidateRetentionRunRequest(now, Apply: true, CandidateRetentionRun.JobTrigger));
            Assert.Equal(0, again.Single(r => r.TenantId == tenantA).DueCount);
            Assert.Equal(0, again.Single(r => r.TenantId == tenantB).DueCount);
        }
        await using (var check = fixture.CreateContext(tenantA))
            Assert.Equal(2, await check.CandidateRetentionRuns.CountAsync());
    }

    [SkippableFact]
    public async Task Legal_hold_committed_while_the_pair_is_being_anonymized_wins()
    {
        // AC-HCM-30i under concurrency: HR sets the citizen row's legal hold in a transaction that is still open when the
        // retention run reaches the pair. The run must wait for it and then leave the pair untouched.
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var now = DateTime.UtcNow;
        var tenant = Guid.NewGuid();
        var seed = await SeedAsync(tenant, duePairs: 1, now.AddMonths(-40));
        var applicationId = seed.DueApplicationIds.Single();

        await using var hr = fixture.CreateContext(tenant);
        await using var holdTransaction = await hr.Database.BeginTransactionAsync();
        Assert.Equal(1, await hr.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "CitizenApplications" SET "LegalHold" = TRUE WHERE "PromotedRecruitmentApplicationId" = {applicationId}"""));

        await using var retentionDb = fixture.CreateContext(tenant);
        var running = Service(retentionDb).RunAsync(
            new CandidateRetentionRunRequest(now, Apply: true, Guid.NewGuid().ToString(), TenantId: tenant));

        // Wait until the run is blocked on the held row (or has finished, which would fail below).
        for (var i = 0; i < 200 && !running.IsCompleted; i++)
        {
            var waiting = await hr.Database.SqlQuery<int>(
                $"""SELECT COUNT(*)::int AS "Value" FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()""")
                .SingleAsync();
            if (waiting > 0)
                break;
            await Task.Delay(50);
        }
        await holdTransaction.CommitAsync();

        var run = Assert.Single(await running);
        Assert.Equal(0, run.AnonymizedCount);
        Assert.Null(run.Error);

        await using var check = fixture.CreateContext(tenant);
        var application = await check.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == applicationId);
        Assert.Null(application.AnonymizedAt);
        var citizen = await check.CitizenApplications.AsNoTracking()
            .SingleAsync(c => c.PromotedRecruitmentApplicationId == applicationId);
        Assert.True(citizen.LegalHold);
        Assert.Null(citizen.AnonymizedAt);
        Assert.NotNull(citizen.Email);
        Assert.Equal(1, await check.CandidateDocuments.CountAsync(d => d.RecruitmentApplicationId == applicationId));
        Assert.Empty(await check.FileReferenceOutbox.AsNoTracking()
            .Where(o => o.Operation == FileReferenceOutboxOps.Release && seed.DueFileIds.Contains(o.FileId)).ToListAsync());
    }

    private async Task AssertTenantAsync(Seed seed)
    {
        await using var db = fixture.CreateContext(seed.TenantId);
        var apps = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => seed.DueApplicationIds.Contains(a.Id)).ToListAsync();
        Assert.Equal(seed.DueApplicationIds.Count, apps.Count);
        Assert.All(apps, a =>
        {
            Assert.NotNull(a.AnonymizedAt);
            Assert.StartsWith("ANON ", a.ApplicantFullName);
            Assert.Null(a.BirthDate);
            Assert.Null(a.RejectionNote);
        });
        var citizens = await db.CitizenApplications.AsNoTracking()
            .Where(c => c.PromotedRecruitmentApplicationId != null && seed.DueApplicationIds.Contains(c.PromotedRecruitmentApplicationId.Value))
            .ToListAsync();
        Assert.Equal(seed.DueApplicationIds.Count, citizens.Count);
        Assert.All(citizens, c =>
        {
            Assert.NotNull(c.AnonymizedAt);
            Assert.Null(c.Email);
            Assert.Null(c.Phone);
            Assert.Null(c.NotificationConsentAt);
        });
        Assert.Equal(0, await db.CandidateDocuments.CountAsync(d => seed.DueApplicationIds.Contains(d.RecruitmentApplicationId)));
        var releases = await db.FileReferenceOutbox.AsNoTracking()
            .Where(o => o.Operation == FileReferenceOutboxOps.Release && seed.DueFileIds.Contains(o.FileId))
            .ToListAsync();
        Assert.Equal(seed.DueFileIds.Count, releases.Count);
        Assert.All(releases, r => Assert.Equal(seed.TenantId, r.TenantId));
        Assert.All(await db.CandidateNotifications.AsNoTracking()
                .Where(n => seed.DueApplicationIds.Contains(n.RecruitmentApplicationId)).ToListAsync(),
            n => Assert.Equal(CandidateRetentionService.NeutralizedText, n.Body));

        foreach (var keptId in seed.KeptApplicationIds)
        {
            var kept = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == keptId);
            Assert.Null(kept.AnonymizedAt);
            Assert.Equal(1, await db.CandidateDocuments.CountAsync(d => d.RecruitmentApplicationId == keptId));
        }
    }

    private static CandidateRetentionService Service(HRCoreDbContext db) =>
        new(db,
            new EfFileReferenceOutbox(db),
            Options.Create(new RecruitmentOptions { Retention = new RecruitmentRetentionOptions { Apply = true } }),
            NullLogger<CandidateRetentionService>.Instance);

    private sealed record Seed(Guid TenantId, List<Guid> DueApplicationIds, List<Guid> DueFileIds, List<Guid> KeptApplicationIds);

    /// <summary>
    /// <paramref name="duePairs"/> public pairs on a requisition closed at <paramref name="closedAt"/>; tenants with more
    /// than 100 also get one pair under legal hold and one pair on an open requisition (both kept).
    /// </summary>
    private async Task<Seed> SeedAsync(Guid tenantId, int duePairs, DateTime closedAt)
    {
        await using var db = fixture.CreateContext(tenantId);
        var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenantId);
        var closed = Hcm026PostgresFixture.NewRequisition(tenantId, position.Id, RequisitionStatus.Closed);
        closed.ClosedDate = closedAt;
        var open = Hcm026PostgresFixture.NewRequisition(tenantId, position.Id, RequisitionStatus.Published);
        db.RecruitmentRequisitions.AddRange(closed, open);
        var postingClosed = new PublicJobPosting { TenantId = tenantId, Title = "محلل", IsPublished = false, RequisitionId = closed.Id };
        var postingOpen = new PublicJobPosting { TenantId = tenantId, Title = "محلل", IsPublished = true, RequisitionId = open.Id };
        db.PublicJobPostings.AddRange(postingClosed, postingOpen);
        await db.SaveChangesAsync();

        var seed = new Seed(tenantId, [], [], []);
        for (var i = 0; i < duePairs; i++)
        {
            var (app, fileId) = AddPair(db, tenantId, closed, postingClosed, i, closedAt.AddDays(-30));
            seed.DueApplicationIds.Add(app.Id);
            seed.DueFileIds.Add(fileId);
        }

        if (duePairs > 100)
        {
            var (held, _) = AddPair(db, tenantId, closed, postingClosed, duePairs, closedAt.AddDays(-30));
            held.LegalHold = true;
            var (openPair, _) = AddPair(db, tenantId, open, postingOpen, duePairs + 1, closedAt.AddYears(-3));
            seed.KeptApplicationIds.Add(held.Id);
            seed.KeptApplicationIds.Add(openPair.Id);
        }

        await db.SaveChangesAsync();
        return seed;
    }

    private static (RecruitmentApplication App, Guid FileId) AddPair(
        HRCoreDbContext db, Guid tenantId, RecruitmentRequisition requisition, PublicJobPosting posting, int index, DateTime appliedAt)
    {
        var nationalId = $"09{index:D9}";
        var app = new RecruitmentApplication
        {
            TenantId = tenantId,
            ApplicantFullName = $"مرشح {index}",
            NationalId = nationalId,
            NationalIdHash = PiiMask.Sha256Hex(nationalId),
            AppliedPosition = requisition.JobTitle,
            RequisitionId = requisition.Id,
            IntakeChannel = RecruitmentIntakeChannel.Public,
            AppliedAt = appliedAt,
            Status = RecruitmentApplicationStatus.Rejected,
            RejectionNote = "لم يستوفِ الشروط",
            BirthDate = new DateOnly(1990, 1, 1),
            Specialization = "محاسبة"
        };
        db.RecruitmentApplications.Add(app);
        var citizen = new CitizenApplication
        {
            TenantId = tenantId,
            PublicJobPostingId = posting.Id,
            RequisitionId = requisition.Id,
            PromotedRecruitmentApplicationId = app.Id,
            ReferenceNumber = $"CA-RET-{tenantId.ToString("N")[..6]}-{index:D4}",
            FullName = app.ApplicantFullName,
            NationalId = nationalId,
            NationalIdHash = app.NationalIdHash,
            Email = $"c{index}@example.sy",
            Phone = "+963999000000",
            NotificationChannel = NotificationChannel.Email,
            NotificationConsentAt = appliedAt,
            SubmittedAt = appliedAt
        };
        db.CitizenApplications.Add(citizen);
        var fileId = Guid.NewGuid();
        db.CandidateDocuments.Add(new CandidateDocument
        {
            TenantId = tenantId,
            RecruitmentApplicationId = app.Id,
            CitizenApplicationId = citizen.Id,
            DocumentType = "CV",
            FileId = fileId,
            OriginalFileName = "cv.pdf",
            ContentType = "application/pdf",
            SizeBytes = 100,
            UploadedAt = appliedAt
        });
        db.CandidateNotifications.Add(new CandidateNotification
        {
            TenantId = tenantId,
            RecruitmentApplicationId = app.Id,
            EventType = RecruitmentApplicationEventTypes.StatusChanged,
            Channel = NotificationChannel.Email,
            TemplateCode = "APP_REJECTED",
            RecipientMasked = PiiMask.Email(citizen.Email),
            Subject = "بوابة التوظيف",
            Body = "نص",
            IdempotencyKey = $"{Guid.NewGuid():D}:Email",
            Status = CandidateNotificationStatus.Sent
        });
        return (app, fileId);
    }
}
