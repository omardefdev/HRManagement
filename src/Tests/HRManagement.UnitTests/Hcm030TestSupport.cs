using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Controllers;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>HCM-030: unified-intake fixtures (SQLite, no interceptors except a save counter).</summary>
internal static class Hcm030TestSupport
{
    public static readonly Guid TenantA = Guid.Parse("30300000-0000-4000-8000-00000000000a");

    private static int _referenceCounter;

    /// <summary>Counts <c>SaveChanges</c> calls that reach the database.</summary>
    public sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Count { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Count++;
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    public static (HRCoreDbContext Db, SaveCounter Saves) CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var saves = new SaveCounter();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(saves)
            .Options;
        var db = new HRCoreDbContext(options);
        db.Database.EnsureCreated();
        return (db, saves);
    }

    public sealed record Fixture(
        RecruitmentRequisition Requisition,
        PublicJobPosting Posting,
        InternalVacancy Vacancy);

    /// <summary>Published requisition (both channels) with its public posting and internal vacancy, in <see cref="TenantA"/>.</summary>
    public static async Task<Fixture> SeedRequisitionAsync(HRCoreDbContext db, string suffix = "A")
    {
        var org = new OrgUnit { TenantId = TenantA, Code = $"OU-H30-{suffix}", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            TenantId = TenantA,
            PositionCode = $"VAC-H30-{suffix}",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            TenantId = TenantA,
            RequisitionNumber = $"REQ-H30-{suffix}",
            FundedPositionId = position.Id,
            JobTitle = "محلل نظم",
            Status = RequisitionStatus.Published,
            PublishingChannel = RecruitmentPublishingChannel.Both,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(req);
        var posting = new PublicJobPosting { TenantId = TenantA, Title = "محلل نظم", IsPublished = true, RequisitionId = req.Id };
        db.PublicJobPostings.Add(posting);
        var vacancy = RecruitmentRequisitionService.MapToInternalVacancy(req, position);
        vacancy.TenantId = TenantA;
        db.InternalVacancies.Add(vacancy);
        req.InternalVacancy = vacancy;
        req.InternalVacancyId = vacancy.Id;
        await db.SaveChangesAsync();
        return new Fixture(req, posting, vacancy);
    }

    public static async Task<Employee> SeedEmployeeAsync(HRCoreDbContext db, string nationalId = "EMP-H30-1")
    {
        var employee = new Employee
        {
            TenantId = TenantA,
            FullName = "موظف مترشّح",
            NationalId = nationalId,
            NationalIdHash = HRManagement.SharedKernel.PiiMask.Sha256Hex(nationalId),
            CurrentStatus = EmploymentStatus.Active,
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    public static CitizenApplicationApplyViewModel ApplyModel(Guid postingId, string nationalId = "0101010101") => new()
    {
        PublicJobPostingId = postingId,
        FullName = "مواطن متقدّم",
        NationalId = nationalId,
        Phone = "0999000000",
        Email = "c@test.sy",
        PrivacyConsent = true
    };

    /// <param name="documentClient">HCM-030 (E2): File.Storage relay (default: unavailable — no file is ever stored).</param>
    public static PublicCareersController CreatePublicCareersController(
        HRCoreDbContext db,
        ICandidateDocumentUploadClient? documentClient = null,
        RecruitmentOptions? options = null)
    {
        var codeGen = new Mock<ICodeGenerator>();
        codeGen.Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new CodeGenerationResult { Code = $"CA-H30-{Interlocked.Increment(ref _referenceCounter):0000}" });
        var recruitmentOptions = Microsoft.Extensions.Options.Options.Create(options ?? new RecruitmentOptions());
        return WithContext(new PublicCareersController(
            db,
            codeGen.Object,
            new RecruitmentApplicationIntakeService(db),
            new UnifiedIntakeService(db, recruitmentOptions),
            new CandidatePublicStatusService(db, recruitmentOptions),
            CreateCandidateDocumentService(db, documentClient),
            recruitmentOptions,
            new HR.Core.Infrastructure.Services.Recruitment.Competitions.CompetitionIntakeService(db, new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())), new RecruitmentSelectionService(db), Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()))), new ClaimsPrincipal(new ClaimsIdentity()));
    }

    /// <summary>HCM-030 (E3): the portal's outbox has no ambient tenant (as in production).</summary>
    public static CandidateDocumentService CreateCandidateDocumentService(
        HRCoreDbContext db, ICandidateDocumentUploadClient? documentClient = null) =>
        new(db, documentClient ?? new UnavailableCandidateDocumentUploadClient(), new EfFileReferenceOutbox(db));

    public static EssTalentController CreateEssController(HRCoreDbContext db, Guid employeeId)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Recruitment:InternalVisibilityScope"] = "All" })
            .Build();
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("employee_id", employeeId.ToString())], "test"));
        return WithContext(new EssTalentController(
            db, new RecruitmentApplicationIntakeService(db), config, new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())), CreatePublicStatus(db),
            new HR.Core.Infrastructure.Services.Recruitment.Competitions.CompetitionIntakeService(db, new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())), new RecruitmentSelectionService(db), Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()))), user);
    }

    public static PublicJobPostingsController CreatePublicJobPostingsController(HRCoreDbContext db) =>
        WithContext(new PublicJobPostingsController(db, CreatePublicStatus(db)), HrUser());

    public static TalentMarketplaceController CreateTalentMarketplaceController(HRCoreDbContext db) =>
        WithContext(new TalentMarketplaceController(db, CreatePublicStatus(db)), HrUser());

    public static CandidatePublicStatusService CreatePublicStatus(HRCoreDbContext db) =>
        new(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));

    /// <summary>
    /// HCM-030 (E2/E6): in-memory File.Storage relay. Uploads succeed (scan PendingScan) until <see cref="FailAfter"/>
    /// files were stored; content is served only for Clean files, like the real service endpoint.
    /// </summary>
    public sealed class FakeCandidateDocumentClient : ICandidateDocumentUploadClient
    {
        public List<(Guid TenantId, PreparedCandidateFile File, Guid FileId)> Uploads { get; } = [];
        public Dictionary<Guid, string> ScanStatuses { get; } = [];
        public List<Guid> ContentRequests { get; } = [];
        public int FailAfter { get; set; } = int.MaxValue;
        public bool MetadataUnavailable { get; set; }

        public Task<CandidateDocumentUploadResult> UploadAsync(Guid tenantId, PreparedCandidateFile file, CancellationToken ct = default)
        {
            if (Uploads.Count >= FailAfter)
                return Task.FromResult<CandidateDocumentUploadResult>(new CandidateDocumentUploadResult.Failed("HTTP 503"));
            var fileId = Guid.NewGuid();
            Uploads.Add((tenantId, file, fileId));
            ScanStatuses[fileId] = CandidateDocumentScanStatus.PendingScan;
            return Task.FromResult<CandidateDocumentUploadResult>(
                new CandidateDocumentUploadResult.Uploaded(fileId, file.ContentType, file.Content.LongLength));
        }

        public Task<IReadOnlyDictionary<Guid, CandidateFileMetadata>?> GetMetadataAsync(
            Guid tenantId, IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default)
        {
            if (MetadataUnavailable)
                return Task.FromResult<IReadOnlyDictionary<Guid, CandidateFileMetadata>?>(null);
            IReadOnlyDictionary<Guid, CandidateFileMetadata> rows = fileIds
                .Where(ScanStatuses.ContainsKey)
                .ToDictionary(id => id, id => new CandidateFileMetadata(
                    id, "file.pdf", "application/pdf", 10, ScanStatuses[id], "Pending", DateTime.UtcNow));
            return Task.FromResult<IReadOnlyDictionary<Guid, CandidateFileMetadata>?>(rows);
        }

        public Task<CandidateFileContentResult> GetContentAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
        {
            ContentRequests.Add(fileId);
            return Task.FromResult<CandidateFileContentResult>(
                ScanStatuses.TryGetValue(fileId, out var status) && status == CandidateDocumentScanStatus.Clean
                    ? new CandidateFileContentResult.Found("%PDF-1.4"u8.ToArray(), "application/pdf", "file.pdf")
                    : new CandidateFileContentResult.NotFound());
        }
    }

    public static T WithContext<T>(T controller, ClaimsPrincipal user) where T : Controller
    {
        var httpContext = new DefaultHttpContext { User = user };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static ClaimsPrincipal HrUser() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, HrManagementRoles.HrCore)], "test"));
}
