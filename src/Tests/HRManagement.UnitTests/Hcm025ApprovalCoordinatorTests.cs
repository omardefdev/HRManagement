using System.Globalization;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Integration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-25 (G-02, decision D-1): an approval always starts — the policy engine may deny or start another workflow, never
/// skip it — and an entity is PendingApproval only together with its workflow instance.
/// </summary>
public sealed class Hcm025ApprovalCoordinatorTests
{
    private const string RequisitionCode = "RECRUITMENT-REQUISITION-APPROVAL";

    public static TheoryData<string, bool> ModesAndScale() => new()
    {
        { "Off", false }, { "Off", true },
        { "Shadow", false }, { "Shadow", true },
        { "Enforce", false }, { "Enforce", true }
    };

    [Theory]
    [MemberData(nameof(ModesAndScale))]
    public async Task Requisition_submit_always_starts_the_workflow(string mode, bool labScale)
    {
        await using var fx = await Fixture.CreateAsync(mode, labScale);
        var req = await fx.SeedDraftRequisitionAsync();

        var (success, error) = await fx.Requisitions.SubmitForApprovalAsync(req, Fixture.Actor, "actor");

        Assert.True(success, error);
        var saved = await fx.Db.RecruitmentRequisitions.AsNoTracking().SingleAsync();
        Assert.Equal(RequisitionStatus.PendingApproval, saved.Status);
        Assert.NotNull(saved.WorkflowInstanceId);
        var instance = await fx.Db.WorkflowInstances.AsNoTracking().SingleAsync();
        Assert.Equal(saved.WorkflowInstanceId, instance.Id);
        Assert.Equal("RecruitmentRequisition", instance.EntityType);
        Assert.Equal(WorkflowStatus.InProgress, instance.Status);
        Assert.Equal(RequisitionCode, (await fx.Db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == instance.DefinitionId)).Code);
    }

    [Fact]
    public async Task Policy_denial_returns_the_reason_and_changes_nothing()
    {
        await using var fx = await Fixture.CreateAsync("Enforce", labScale: false,
            policy: new PolicyEnforceResponse { Allowed = false, Decision = new PolicyDecision { Allowed = false, DenyReason = "مرفوض للاختبار" } });
        var req = await fx.SeedDraftRequisitionAsync();

        var (success, error) = await fx.Requisitions.SubmitForApprovalAsync(req, Fixture.Actor, "actor");

        Assert.False(success);
        Assert.Equal("مرفوض للاختبار", error);
        var saved = await fx.Db.RecruitmentRequisitions.AsNoTracking().SingleAsync();
        Assert.Equal(RequisitionStatus.Draft, saved.Status);
        Assert.Null(saved.WorkflowInstanceId);
        Assert.Empty(await fx.Db.WorkflowInstances.ToListAsync());
    }

    [Fact]
    public async Task Policy_started_instance_is_used_and_no_second_instance_is_started()
    {
        var policyInstanceId = Guid.NewGuid();
        await using var fx = await Fixture.CreateAsync("Enforce", labScale: false,
            policy: new PolicyEnforceResponse { Allowed = true, WorkflowInstanceId = policyInstanceId });
        var req = await fx.SeedDraftRequisitionAsync();

        var (success, error) = await fx.Requisitions.SubmitForApprovalAsync(req, Fixture.Actor, "actor");

        Assert.True(success, error);
        Assert.Equal(policyInstanceId, (await fx.Db.RecruitmentRequisitions.AsNoTracking().SingleAsync()).WorkflowInstanceId);
        Assert.Empty(await fx.Db.WorkflowInstances.ToListAsync());
    }

    [Fact]
    public async Task Missing_definition_is_a_clear_error_and_the_requisition_stays_draft()
    {
        await using var fx = await Fixture.CreateAsync("Off", labScale: true, seedDefinitions: false);
        var req = await fx.SeedDraftRequisitionAsync();

        var (success, error) = await fx.Requisitions.SubmitForApprovalAsync(req, Fixture.Actor, "actor");

        Assert.False(success);
        Assert.Equal(ApprovalStartCoordinator.MissingDefinitionMessage, error);
        var saved = await fx.Db.RecruitmentRequisitions.AsNoTracking().SingleAsync();
        Assert.Equal(RequisitionStatus.Draft, saved.Status);
        Assert.Null(saved.WorkflowInstanceId);
    }

    [Fact]
    public async Task An_in_progress_instance_of_the_entity_is_reused_never_two()
    {
        await using var fx = await Fixture.CreateAsync("Off", labScale: false);
        var req = await fx.SeedDraftRequisitionAsync();
        var request = new ApprovalStartRequest("RecruitmentRequisition", req.Id, req.TenantId, RequisitionCode,
            PolicyDomains.CoreRecruitment, Fixture.Actor, "actor", "{}");

        var first = await fx.Coordinator.StartAsync(request);
        var second = await fx.Coordinator.StartAsync(request);

        Assert.True(first.Started);
        Assert.Equal(first.InstanceId, second.InstanceId);
        Assert.Single(await fx.Db.WorkflowInstances.ToListAsync());
    }

    [Fact]
    public async Task Always_start_disabled_refuses_the_submit_instead_of_leaving_it_without_workflow()
    {
        await using var fx = await Fixture.CreateAsync("Off", labScale: false,
            extra: new() { ["Recruitment:Approvals:AlwaysStartWorkflow"] = "false" });
        var req = await fx.SeedDraftRequisitionAsync();

        var (success, error) = await fx.Requisitions.SubmitForApprovalAsync(req, Fixture.Actor, "actor");

        Assert.False(success);
        Assert.Equal(ApprovalStartCoordinator.StartDisabledMessage, error);
        Assert.Equal(RequisitionStatus.Draft, (await fx.Db.RecruitmentRequisitions.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Fail_if_definition_missing_false_reports_null_from_the_coordinator_but_submit_still_refuses()
    {
        await using var fx = await Fixture.CreateAsync("Off", labScale: false, seedDefinitions: false,
            extra: new() { ["Recruitment:Approvals:FailIfDefinitionMissing"] = "false" });
        var req = await fx.SeedDraftRequisitionAsync();

        var direct = await fx.Coordinator.StartAsync(new ApprovalStartRequest("RecruitmentRequisition", req.Id, req.TenantId,
            RequisitionCode, PolicyDomains.CoreRecruitment, Fixture.Actor, "actor", "{}"));
        var (success, error) = await fx.Requisitions.SubmitForApprovalAsync(req, Fixture.Actor, "actor");

        Assert.Null(direct.InstanceId);
        Assert.Null(direct.Error);
        Assert.False(success);
        Assert.Equal(ApprovalStartCoordinator.MissingDefinitionMessage, error);
        Assert.Equal(RequisitionStatus.Draft, (await fx.Db.RecruitmentRequisitions.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Restart_gives_a_stuck_requisition_its_workflow_and_audits_it()
    {
        await using var fx = await Fixture.CreateAsync("Off", labScale: false);
        var req = await fx.SeedDraftRequisitionAsync(RequisitionStatus.PendingApproval);

        var (success, error) = await fx.Requisitions.RestartApprovalAsync(req.Id, Fixture.Actor, "actor");
        var again = await fx.Requisitions.RestartApprovalAsync(req.Id, Fixture.Actor, "actor");

        Assert.True(success, error);
        var saved = await fx.Db.RecruitmentRequisitions.AsNoTracking().SingleAsync();
        Assert.Equal(RequisitionStatus.PendingApproval, saved.Status);
        Assert.NotNull(saved.WorkflowInstanceId);
        Assert.Single(await fx.Db.AuditLogs.Where(a => a.Action == ApprovalRestartRules.RestartAuditAction).ToListAsync());
        Assert.False(again.Success);
        Assert.Equal(ApprovalRestartRules.NotStuckMessage, again.Error);
        Assert.Single(await fx.Db.WorkflowInstances.ToListAsync());
    }

    [Theory]
    [InlineData(false, "RECRUITMENT-OFFER-APPROVAL")]
    [InlineData(true, "RECRUITMENT-OFFER-OVERRIDE-APPROVAL")]
    public async Task Offer_submit_starts_the_normal_or_override_workflow_outside_lab_scale(bool exceptionOffer, string expectedCode)
    {
        await using var fx = await Fixture.CreateAsync("Off", labScale: false);
        var offer = await fx.SeedDraftOfferAsync(exceptionOffer);

        var (success, error) = await fx.Offers.SubmitForApprovalAsync(offer.Id, Fixture.Actor, "actor");

        Assert.True(success, error);
        var saved = await fx.Db.OfferLetters.AsNoTracking().SingleAsync();
        Assert.Equal(OfferStatus.PendingApproval, saved.Status);
        var instance = await fx.Db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == saved.WorkflowInstanceId);
        Assert.Equal(expectedCode, (await fx.Db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == instance.DefinitionId)).Code);
    }

    [Fact]
    public async Task Offer_submit_without_definition_stays_draft()
    {
        await using var fx = await Fixture.CreateAsync("Shadow", labScale: false, seedDefinitions: false);
        var offer = await fx.SeedDraftOfferAsync(exceptionOffer: false);

        var (success, error) = await fx.Offers.SubmitForApprovalAsync(offer.Id, Fixture.Actor, "actor");

        Assert.False(success);
        Assert.Equal(ApprovalStartCoordinator.MissingDefinitionMessage, error);
        var saved = await fx.Db.OfferLetters.AsNoTracking().SingleAsync();
        Assert.Equal(OfferStatus.Draft, saved.Status);
        Assert.Null(saved.WorkflowInstanceId);
    }

    [Theory]
    [InlineData("Off")]
    [InlineData("Shadow")]
    public async Task Establishment_change_outside_lab_scale_waits_for_approval_instead_of_being_applied(string mode)
    {
        await using var fx = await Fixture.CreateAsync(mode, labScale: false, establishmentMode: mode);
        var request = await fx.SeedDraftEstablishmentChangeAsync();

        var (success, error) = await fx.Establishment.SubmitForApprovalAsync(
            request.Id, Syr001LegislationIds.Law50_2004, "DEC-25", Fixture.Actor, "actor");

        Assert.True(success, error);
        var saved = await fx.Db.EstablishmentChangeRequests.AsNoTracking().SingleAsync();
        Assert.Equal(EstablishmentChangeStatus.PendingApproval, saved.Status);
        Assert.NotNull(saved.WorkflowInstanceId);
        Assert.Equal("DEC-25", saved.DecisionReference);
        fx.Applier.Verify(a => a.ApplyAsync(It.IsAny<EstablishmentChangeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Establishment_change_without_definition_stays_draft_and_keeps_its_fields()
    {
        await using var fx = await Fixture.CreateAsync("Off", labScale: false, seedDefinitions: false);
        var request = await fx.SeedDraftEstablishmentChangeAsync();

        var (success, error) = await fx.Establishment.SubmitForApprovalAsync(
            request.Id, Syr001LegislationIds.Law50_2004, "DEC-25", Fixture.Actor, "actor");

        Assert.False(success);
        Assert.Equal(ApprovalStartCoordinator.MissingDefinitionMessage, error);
        var saved = await fx.Db.EstablishmentChangeRequests.AsNoTracking().SingleAsync();
        Assert.Equal(EstablishmentChangeStatus.Draft, saved.Status);
        Assert.Null(saved.WorkflowInstanceId);
        Assert.Null(saved.DecisionReference);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly Guid Actor = Guid.Parse("25250000-0000-4000-8000-0000000000a1");

        private Fixture(HRCoreDbContext db, IConfiguration config, IPolicyOrchestrator policy)
        {
            Db = db;
            var starter = new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance);
            var legal = new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db));
            Coordinator = new ApprovalStartCoordinator(db, policy, starter, config);
            Requisitions = new RecruitmentRequisitionLifecycleService(
                db, policy, legal, starter, config, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));
            Offers = new RecruitmentOfferService(
                db, policy, Mock.Of<ICodeGenerator>(), legal, starter, config,
                Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()), Mock.Of<IPayrollGradeLookup>());
            Applier = new Mock<IEstablishmentChangeApplier>();
            Establishment = new EstablishmentChangeLifecycleService(
                db, Mock.Of<ICodeGenerator>(), policy, new EstablishmentLegalSourceValidator(legal), Applier.Object, starter, config);
        }

        public HRCoreDbContext Db { get; }
        public ApprovalStartCoordinator Coordinator { get; }
        public RecruitmentRequisitionLifecycleService Requisitions { get; }
        public RecruitmentOfferService Offers { get; }
        public EstablishmentChangeLifecycleService Establishment { get; }
        public Mock<IEstablishmentChangeApplier> Applier { get; }

        public static async Task<Fixture> CreateAsync(
            string mode,
            bool labScale,
            bool seedDefinitions = true,
            PolicyEnforceResponse? policy = null,
            Dictionary<string, string?>? extra = null,
            string? establishmentMode = null)
        {
            var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            await LegislationRegisterSeeder.SeedAsync(db);
            if (seedDefinitions)
                await WorkflowPackSeeder.SeedAsync(db);

            var values = new Dictionary<string, string?>
            {
                ["PolicyEngine:Core.Recruitment"] = mode,
                ["PolicyEngine:Core.Establishment"] = establishmentMode ?? mode,
                ["NationalScope:IsLabScale"] = labScale.ToString(CultureInfo.InvariantCulture)
            };
            foreach (var (key, value) in extra ?? [])
                values[key] = value;
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

            // Shadow answers Allowed without an instance; Enforce without a trigger the same — the coordinator starts it.
            var orchestrator = new Mock<IPolicyOrchestrator>();
            orchestrator.Setup(o => o.EnforceAsync(It.IsAny<PolicyEnforceRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(policy ?? new PolicyEnforceResponse { Allowed = true });

            return new Fixture(db, config, orchestrator.Object);
        }

        public async Task<RecruitmentRequisition> SeedDraftRequisitionAsync(RequisitionStatus status = RequisitionStatus.Draft)
        {
            var position = await SeedPositionAsync();
            var req = new RecruitmentRequisition
            {
                RequisitionNumber = "REQ-25-001",
                FundedPositionId = position.Id,
                JobTitle = "محلل",
                Status = status,
                RequestedDate = DateTime.UtcNow,
                VacancyCount = 1,
                LegalSourceId = Syr001LegislationIds.Law50_2004
            };
            Db.RecruitmentRequisitions.Add(req);
            await Db.SaveChangesAsync();
            return req;
        }

        public async Task<OfferLetter> SeedDraftOfferAsync(bool exceptionOffer)
        {
            var req = await SeedDraftRequisitionAsync(RequisitionStatus.Published);
            var app = new RecruitmentApplication
            {
                RequisitionId = req.Id,
                ApplicantFullName = "مرشح",
                Status = RecruitmentApplicationStatus.OfferExtended,
                AppliedAt = DateTime.UtcNow
            };
            Db.RecruitmentApplications.Add(app);
            var offer = new OfferLetter
            {
                OfferNumber = "OFF-25-001",
                RecruitmentApplicationId = app.Id,
                FundedPositionId = req.FundedPositionId,
                Status = OfferStatus.Draft,
                LegalSourceId = Syr001LegislationIds.Law50_2004,
                IsMeritOverride = exceptionOffer
            };
            Db.OfferLetters.Add(offer);
            await Db.SaveChangesAsync();
            return offer;
        }

        public async Task<EstablishmentChangeRequest> SeedDraftEstablishmentChangeAsync()
        {
            var request = new EstablishmentChangeRequest
            {
                RequestNumber = "EST-25-001",
                ChangeType = EstablishmentChangeType.CreateJobCatalog,
                Status = EstablishmentChangeStatus.Draft,
                PayloadJson = """{"JobCode":"","TitleAr":"محلل","GradeCode":"G5","OccupationalGroup":"JC-ADM"}""",
                RequestedAt = DateTime.UtcNow
            };
            Db.EstablishmentChangeRequests.Add(request);
            await Db.SaveChangesAsync();
            return request;
        }

        private async Task<FundedPosition> SeedPositionAsync()
        {
            var org = new OrgUnit { Code = "OU-25-" + Guid.NewGuid().ToString("N")[..6], NameAr = "وحدة", NameEn = "Unit" };
            Db.OrgUnits.Add(org);
            var position = new FundedPosition
            {
                PositionCode = "VAC-25-" + Guid.NewGuid().ToString("N")[..6],
                GradeCode = "G5",
                BudgetYear = 2026,
                Status = FundedPositionStatus.Vacant,
                OrgUnitId = org.Id
            };
            Db.FundedPositions.Add(position);
            await Db.SaveChangesAsync();
            return position;
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }
}
