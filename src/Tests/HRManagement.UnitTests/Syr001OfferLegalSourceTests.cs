using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using HR.Core.Infrastructure.Services.Policy;
using Moq;

namespace HRManagement.UnitTests;

public class Syr001OfferLegalSourceTests
{
    private static async Task<(HRCoreDbContext Db, RecruitmentOfferService OfferService)> CreateOfferServiceAsync()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new HRCoreDbContext(options);
        await LegislationRegisterSeeder.SeedAsync(db);

        var provider = new LegislationRegisterProvider(db);
        var validator = new RecruitmentLegalSourceValidator(provider);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyEngine:DefaultMode"] = "Off" })
            .Build();

        var codeGenerator = new Mock<ICodeGenerator>();
        codeGenerator
            .Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeGenerationResult { Code = "OFF-TEST-001" });

        var offerService = new RecruitmentOfferService(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            codeGenerator.Object,
            validator,
            Mock.Of<IWorkflowStarter>(),
            config);

        return (db, offerService);
    }

    [Fact]
    public void LineageMatches_allows_null_requisition()
    {
        Assert.True(RecruitmentLegalSourceRules.LineageMatches(null, Syr001LegislationIds.Law50_2004));
        Assert.True(RecruitmentLegalSourceRules.LineageMatches("   ", Syr001LegislationIds.Law50_2004));
    }

    [Fact]
    public void LineageMatches_rejects_mismatch()
    {
        Assert.False(RecruitmentLegalSourceRules.LineageMatches(
            Syr001LegislationIds.Law50_2004,
            Syr001LegislationIds.Law53_2021));
    }

    [Fact]
    public async Task CreateDraft_copies_legal_source_from_requisition()
    {
        var (db, offerService) = await CreateOfferServiceAsync();
        var app = await SeedRankedApplicationAsync(db, Syr001LegislationIds.Law50_2004);

        var (offer, error) = await offerService.CreateDraftAsync(
            app.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null);

        Assert.Null(error);
        Assert.NotNull(offer);
        Assert.Equal(Syr001LegislationIds.Law50_2004, offer!.LegalSourceId);
    }

    [Fact]
    public async Task CreateDraft_fails_when_requisition_missing_legal_source()
    {
        var (db, offerService) = await CreateOfferServiceAsync();
        var app = await SeedRankedApplicationAsync(db, legalSourceId: null);

        var (offer, error) = await offerService.CreateDraftAsync(
            app.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null);

        Assert.Null(offer);
        Assert.Equal(RecruitmentLegalSourceRules.RequisitionMissingLegalSourceForOfferMessage, error);
    }

    [Fact]
    public async Task OfferSubmit_fails_without_legal_source()
    {
        var (db, offerService) = await CreateOfferServiceAsync();
        var app = await SeedRankedApplicationAsync(db, Syr001LegislationIds.Law50_2004);
        var offer = new OfferLetter
        {
            RecruitmentApplicationId = app.Id,
            FundedPositionId = app.Requisition!.FundedPositionId,
            OfferNumber = "OFF-NO-LS",
            Status = OfferStatus.Draft,
            OfferedSalary = 5000,
            ProposedStartDate = DateTime.UtcNow.AddDays(30)
        };
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();

        var (success, error) = await offerService.SubmitForApprovalAsync(offer.Id, Guid.NewGuid(), "test");

        Assert.False(success);
        Assert.Equal(RecruitmentLegalSourceRules.RequiredOnOfferSubmitMessage, error);
    }

    [Fact]
    public async Task OfferSubmit_succeeds_with_law50()
    {
        var (db, offerService) = await CreateOfferServiceAsync();
        var app = await SeedRankedApplicationAsync(db, Syr001LegislationIds.Law50_2004);

        var (offer, createError) = await offerService.CreateDraftAsync(
            app.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null);
        Assert.Null(createError);

        var (success, error) = await offerService.SubmitForApprovalAsync(offer!.Id, Guid.NewGuid(), "test");

        Assert.True(success, error);
        Assert.Equal(OfferStatus.Issued, (await db.OfferLetters.SingleAsync()).Status);
    }

    private static async Task<RecruitmentApplication> SeedRankedApplicationAsync(
        HRCoreDbContext db,
        string? legalSourceId)
    {
        var org = new OrgUnit { Code = "OU-OFF", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-OFF",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-OFF-001",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Published,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            LegalSourceId = legalSourceId
        };
        db.RecruitmentRequisitions.Add(req);
        var app = new RecruitmentApplication
        {
            ApplicantFullName = "مرشح",
            RequisitionId = req.Id,
            Requisition = req,
            AppliedPosition = req.JobTitle,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Ranked
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();
        return app;
    }
}
