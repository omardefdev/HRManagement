using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// HCM-030 (C, AC-HCM-30j): <c>scripts/sql/hcm030-backfill-unified-intake.sql</c> on a migrated PostgreSQL links every
/// requisition-linked channel row without a recruitment application, exactly as the C# mapping
/// (<see cref="UnifiedIntakeStatusMapping"/>) would, is idempotent, never dates a rejection "now", records no events
/// and respects the unique link indexes. Own container (class fixture): the script reads the whole database.
/// </summary>
[Trait("Category", "Testcontainers")]
public class Hcm030BackfillTests(Hcm026PostgresFixture fixture) : IClassFixture<Hcm026PostgresFixture>
{
    private static readonly DateTime SourceUpdatedAt = new(2026, 8, 10, 8, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime EndedAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task Backfill_links_unlinked_channel_rows_like_the_csharp_mapping_and_is_idempotent()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason);
        var tenant = Guid.NewGuid();
        var seed = await SeedAsync(tenant);

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var eventsBefore = await ScalarAsync(connection, "SELECT COUNT(*) FROM \"RecruitmentApplicationEvents\";");
        var appsBefore = await ScalarAsync(connection, "SELECT COUNT(*) FROM \"RecruitmentApplications\";");

        var reported = await RunBackfillAsync(connection);

        // Counts: every unlinked requisition-linked row, nothing else.
        var expectedLinked = seed.Citizens.Count + seed.Internals.Count;
        Assert.Equal(expectedLinked, reported);
        Assert.Equal(appsBefore + expectedLinked, await ScalarAsync(connection, "SELECT COUNT(*) FROM \"RecruitmentApplications\";"));
        Assert.Equal(eventsBefore, await ScalarAsync(connection, "SELECT COUNT(*) FROM \"RecruitmentApplicationEvents\";"));

        await using var db = fixture.CreateContext(tenant);
        var requisitions = await db.RecruitmentRequisitions.AsNoTracking().ToDictionaryAsync(r => r.Id);
        foreach (var source in seed.Citizens)
        {
            var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == source.Id);
            var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == citizen.PromotedRecruitmentApplicationId);
            var expected = UnifiedIntakeStatusMapping.Map(source.Status, SourceUpdatedAt, requisitions[source.RequisitionId!.Value]);
            AssertMatches(expected, app, $"citizen {source.Status} on {requisitions[source.RequisitionId.Value].Status}");
            Assert.Equal(RecruitmentIntakeChannel.Public, app.IntakeChannel);
            Assert.Equal($"تقديم عبر البوابة العامة {source.ReferenceNumber}", StripNote(app.Notes, expected));
            Assert.Equal(source.SubmittedAt, app.AppliedAt, TimeSpan.FromMilliseconds(1));
            Assert.Equal(tenant, app.TenantId);
            Assert.Equal(source.NationalIdHash, app.NationalIdHash);
            Assert.Equal(source.NationalId, app.NationalId);
            Assert.Equal(source.ConflictOfInterestDeclared, app.ConflictOfInterestDeclared);
            Assert.Null(app.SourceInternalApplicationId);
        }

        foreach (var source in seed.Internals)
        {
            var internalApp = await db.InternalApplications.AsNoTracking().SingleAsync(i => i.Id == source.Id);
            var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == internalApp.PromotedRecruitmentApplicationId);
            var expected = UnifiedIntakeStatusMapping.Map(source.Status, SourceUpdatedAt, requisitions[source.RequisitionId!.Value]);
            AssertMatches(expected, app, $"internal {source.Status} on {requisitions[source.RequisitionId.Value].Status}");
            Assert.Equal(RecruitmentIntakeChannel.Internal, app.IntakeChannel);
            Assert.Equal(source.Id, app.SourceInternalApplicationId);
            Assert.Equal(source.AppliedAt, app.AppliedAt, TimeSpan.FromMilliseconds(1));
            // Employees.NationalId is encrypted at rest: only the hash is copied.
            Assert.Null(app.NationalId);
            Assert.Equal(seed.EmployeeHashes[source.EmployeeId], app.NationalIdHash);
        }

        // Rejections keep historic dates: no appeal window reopens.
        var rejectedAt = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.RejectedAt != null && a.Status == RecruitmentApplicationStatus.Rejected)
            .Select(a => a.RejectedAt!.Value)
            .ToListAsync();
        Assert.NotEmpty(rejectedAt);
        Assert.All(rejectedAt, at => Assert.True(at < DateTime.UtcNow.AddDays(-7), $"RejectedAt {at:O} is recent"));

        // Already linked and legacy rows are untouched.
        Assert.Equal(seed.AlreadyLinkedTarget,
            (await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == seed.AlreadyLinkedId)).PromotedRecruitmentApplicationId);
        Assert.Null((await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == seed.LegacyId)).PromotedRecruitmentApplicationId);

        // Idempotent: a second run links nothing.
        Assert.Equal(0, await RunBackfillAsync(connection));
        Assert.Equal(appsBefore + expectedLinked, await ScalarAsync(connection, "SELECT COUNT(*) FROM \"RecruitmentApplications\";"));

        // The unique filtered link indexes hold.
        Assert.Equal(0L, await ScalarAsync(connection, """
            SELECT COUNT(*) FROM (SELECT "PromotedRecruitmentApplicationId" FROM "CitizenApplications"
                                  WHERE "PromotedRecruitmentApplicationId" IS NOT NULL GROUP BY 1 HAVING COUNT(*) > 1) d;
            """));
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"""
            UPDATE "CitizenApplications" SET "PromotedRecruitmentApplicationId" = '{seed.AlreadyLinkedTarget}'
            WHERE "Id" = '{seed.LegacyId}';
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var internalDuplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"""
            UPDATE "InternalApplications" SET "PromotedRecruitmentApplicationId" =
                (SELECT "PromotedRecruitmentApplicationId" FROM "InternalApplications" WHERE "Id" = '{seed.Internals[0].Id}')
            WHERE "Id" = '{seed.Internals[1].Id}';
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, internalDuplicate.SqlState);
    }

    private static void AssertMatches(UnifiedIntakeLinkState expected, RecruitmentApplication app, string row)
    {
        Assert.True(expected.Status == app.Status, $"{row}: status {app.Status}, expected {expected.Status}");
        Assert.True(expected.RejectionKind == app.RejectionKind, $"{row}: kind {app.RejectionKind}, expected {expected.RejectionKind}");
        AssertSameTime(expected.RejectedAt, app.RejectedAt, row + " RejectedAt");
        AssertSameTime(expected.ScreenedAt, app.ScreenedAt, row + " ScreenedAt");
        AssertSameTime(expected.ReviewedAt, app.ReviewedAt, row + " ReviewedAt");
        Assert.Equal(RecruitmentAppealStatus.None, app.AppealStatus);
        Assert.Equal("محلل", app.AppliedPosition);
        Assert.Equal(expected.Note is not null, app.Notes!.EndsWith(" — " + UnifiedIntakeStatusMapping.AcceptedNote));
    }

    private static void AssertSameTime(DateTime? expected, DateTime? actual, string what)
    {
        Assert.True(expected.HasValue == actual.HasValue, $"{what}: {actual:O}, expected {expected:O}");
        if (expected is DateTime e)
            Assert.Equal(e, actual!.Value, TimeSpan.FromMilliseconds(1));
    }

    private static string StripNote(string? notes, UnifiedIntakeLinkState expected) =>
        expected.Note is null ? notes! : notes![..^(" — " + expected.Note).Length];

    private sealed record Seed(
        List<CitizenApplication> Citizens,
        List<InternalApplication> Internals,
        Dictionary<Guid, string> EmployeeHashes,
        Guid AlreadyLinkedId,
        Guid AlreadyLinkedTarget,
        Guid LegacyId);

    private async Task<Seed> SeedAsync(Guid tenant)
    {
        await using var db = fixture.CreateContext(tenant);
        var published = await RequisitionAsync(db, tenant, RequisitionStatus.Published);
        var cancelled = await RequisitionAsync(db, tenant, RequisitionStatus.Cancelled);
        var closed = await RequisitionAsync(db, tenant, RequisitionStatus.Closed);
        var postings = new Dictionary<Guid, PublicJobPosting>();
        foreach (var req in new[] { published, cancelled, closed })
        {
            var posting = new PublicJobPosting { TenantId = tenant, Title = "محلل", IsPublished = true, RequisitionId = req.Id };
            db.PublicJobPostings.Add(posting);
            postings[req.Id] = posting;
        }

        var citizens = new List<CitizenApplication>();
        var n = 0;
        void Citizen(RecruitmentRequisition req, CitizenApplicationStatus status, bool coi = false)
        {
            n++;
            var nationalId = $"H30-{tenant.ToString("N")[..6]}-{n:D2}";
            var citizen = new CitizenApplication
            {
                TenantId = tenant,
                PublicJobPostingId = postings[req.Id].Id,
                RequisitionId = req.Id,
                ReferenceNumber = $"CA-H30-{tenant.ToString("N")[..6]}-{n:D2}",
                FullName = $"مواطن {n}",
                NationalId = nationalId,
                NationalIdHash = PiiMask.Sha256Hex(nationalId),
                Status = status,
                SubmittedAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc).AddDays(n),
                UpdatedAt = SourceUpdatedAt,
                ConflictOfInterestDeclared = coi,
                ConflictOfInterestDeclaredAt = coi ? SourceUpdatedAt : null
            };
            db.CitizenApplications.Add(citizen);
            citizens.Add(citizen);
        }

        foreach (var status in Enum.GetValues<CitizenApplicationStatus>())
            Citizen(published, status, coi: status == CitizenApplicationStatus.Shortlisted);
        Citizen(cancelled, CitizenApplicationStatus.Received);
        Citizen(cancelled, CitizenApplicationStatus.Shortlisted);
        Citizen(cancelled, CitizenApplicationStatus.Rejected);
        Citizen(closed, CitizenApplicationStatus.UnderReview);
        Citizen(closed, CitizenApplicationStatus.Accepted);

        var position = await db.FundedPositions.FirstAsync(p => p.Id == published.FundedPositionId);
        var vacancy = RecruitmentRequisitionService.MapToInternalVacancy(published, position);
        vacancy.TenantId = tenant;
        db.InternalVacancies.Add(vacancy);
        var cancelledPosition = await db.FundedPositions.FirstAsync(p => p.Id == cancelled.FundedPositionId);
        var cancelledVacancy = RecruitmentRequisitionService.MapToInternalVacancy(cancelled, cancelledPosition);
        cancelledVacancy.TenantId = tenant;
        db.InternalVacancies.Add(cancelledVacancy);

        var internals = new List<InternalApplication>();
        var hashes = new Dictionary<Guid, string>();
        void Internal(InternalVacancy v, RecruitmentRequisition req, InternalApplicationStatus status)
        {
            n++;
            var nationalId = $"E30-{tenant.ToString("N")[..6]}-{n:D2}";
            var employee = new Employee
            {
                TenantId = tenant,
                FullName = $"موظف {n}",
                NationalId = nationalId,
                NationalIdHash = PiiMask.Sha256Hex(nationalId),
                BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            db.Employees.Add(employee);
            hashes[employee.Id] = employee.NationalIdHash!;
            var app = new InternalApplication
            {
                TenantId = tenant,
                InternalVacancyId = v.Id,
                EmployeeId = employee.Id,
                RequisitionId = req.Id,
                Status = status,
                AppliedAt = new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc).AddDays(n),
                UpdatedAt = SourceUpdatedAt
            };
            db.InternalApplications.Add(app);
            internals.Add(app);
        }

        foreach (var status in Enum.GetValues<InternalApplicationStatus>())
            Internal(vacancy, published, status);
        Internal(cancelledVacancy, cancelled, InternalApplicationStatus.UnderReview);

        // Already linked (after unification) and legacy (no requisition) rows must stay as they are.
        var linkedApp = new RecruitmentApplication
        {
            TenantId = tenant,
            ApplicantFullName = "مرتبط",
            RequisitionId = published.Id,
            AppliedPosition = "محلل",
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.UnderReview,
            IntakeChannel = RecruitmentIntakeChannel.Public
        };
        db.RecruitmentApplications.Add(linkedApp);
        var alreadyLinked = new CitizenApplication
        {
            TenantId = tenant,
            PublicJobPostingId = postings[published.Id].Id,
            RequisitionId = published.Id,
            ReferenceNumber = $"CA-H30-{tenant.ToString("N")[..6]}-LNK",
            FullName = "مرتبط",
            NationalId = $"L30-{tenant.ToString("N")[..6]}",
            Status = CitizenApplicationStatus.Received,
            PromotedRecruitmentApplication = linkedApp
        };
        db.CitizenApplications.Add(alreadyLinked);
        var legacyPosting = new PublicJobPosting { TenantId = tenant, Title = "قديم", IsPublished = true };
        db.PublicJobPostings.Add(legacyPosting);
        var legacy = new CitizenApplication
        {
            TenantId = tenant,
            PublicJobPostingId = legacyPosting.Id,
            ReferenceNumber = $"CA-H30-{tenant.ToString("N")[..6]}-OLD",
            FullName = "قديم",
            NationalId = $"O30-{tenant.ToString("N")[..6]}",
            Status = CitizenApplicationStatus.UnderReview
        };
        db.CitizenApplications.Add(legacy);
        await db.SaveChangesAsync();

        return new Seed(citizens, internals, hashes, alreadyLinked.Id, linkedApp.Id, legacy.Id);
    }

    private static async Task<RecruitmentRequisition> RequisitionAsync(HRCoreDbContext db, Guid tenant, RequisitionStatus status)
    {
        var position = await Hcm026PostgresFixture.SeedVacantPositionAsync(db, tenant);
        var req = Hcm026PostgresFixture.NewRequisition(tenant, position.Id, status);
        req.PublishingChannel = RecruitmentPublishingChannel.Both;
        req.CancelledAt = status == RequisitionStatus.Cancelled ? EndedAt : null;
        req.ClosedDate = status == RequisitionStatus.Closed ? EndedAt : null;
        db.RecruitmentRequisitions.Add(req);
        await db.SaveChangesAsync();
        return req;
    }

    private string ConnectionString()
    {
        using var db = fixture.CreateContext(Guid.NewGuid());
        return db.Database.GetConnectionString()!;
    }

    /// <summary>Runs the SQL file as the PowerShell wrapper does (-Apply); returns the number of reported rows.</summary>
    private static async Task<int> RunBackfillAsync(NpgsqlConnection connection)
    {
        var sql = await System.IO.File.ReadAllTextAsync(Path.Combine(RepoRoot(), "scripts", "sql", "hcm030-backfill-unified-intake.sql"));
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = 0;
        do
        {
            while (await reader.ReadAsync())
                rows++;
        } while (await reader.NextResultAsync());
        return rows;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "scripts", "sql", "hcm030-backfill-unified-intake.sql")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
