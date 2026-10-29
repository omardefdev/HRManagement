using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-25 (G-02 / G-04): the real HR.Core host on <b>PostgreSQL</b> with the Production values that drive recruitment
/// approvals — <c>NationalScope:IsLabScale=false</c>, <c>PolicyEngine:Core.Recruitment=Shadow</c>, no establishment policy
/// mode (Off), <c>Recruitment:Approvals:AlwaysStartWorkflow=true</c>, <c>Workflow:AdminBypass=false</c>. Every approval is
/// driven over HTTP by a distinct user holding only the step's role. The environment stays Development so the start-up
/// guards that need production secrets do not run; the values under test are set explicitly.
/// </summary>
[Trait("Category", "Testcontainers")]
[Collection("IntegrationSerial")]
public sealed class Hcm025ProductionApprovalIntegrationTests(
    WebApplicationFactory<HRCoreWebProgram> root,
    Hcm025ProductionApprovalIntegrationTests.PostgresFixture postgres)
    : IClassFixture<WebApplicationFactory<HRCoreWebProgram>>,
      IClassFixture<Hcm025ProductionApprovalIntegrationTests.PostgresFixture>,
      IDisposable
{
    private static readonly Guid Submitter = Guid.Parse("25250000-0000-4000-8000-0000000000c1");
    private static readonly Guid StepOneApprover = Guid.Parse("25250000-0000-4000-8000-0000000000a1");
    private static readonly Guid QuorumApprover = Guid.Parse("25250000-0000-4000-8000-0000000000a2");
    private static readonly Guid ManagementApprover = Guid.Parse("25250000-0000-4000-8000-0000000000b1");

    private static readonly Regex AntiforgeryTokenPattern = new(
        "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private WebApplicationFactory<HRCoreWebProgram>? _host;

    [SkippableFact]
    public async Task Requisition_submit_starts_the_workflow_and_two_approvals_approve_it()
    {
        var host = await HostAsync();
        var seed = await SeedAsync(host, RequisitionStatus.Draft);
        var id = seed.RequisitionId;

        await PostAsync(host, Submitter, [HrManagementRoles.HrCore],
            $"/RecruitmentRequisitions/Details/{id:D}", $"/RecruitmentRequisitions/Submit/{id:D}", []);
        var pending = await WithDbAsync(host, db => db.RecruitmentRequisitions.AsNoTracking().SingleAsync(r => r.Id == id));
        Assert.Equal(RequisitionStatus.PendingApproval, pending.Status);
        Assert.NotNull(pending.WorkflowInstanceId);

        await ApproveAsync(host, StepOneApprover, [HrManagementRoles.HrCore], "RecruitmentRequisitions", "RequisitionId", id);
        await ApproveAsync(host, ManagementApprover, [HrManagementRoles.HrManagementApprover], "RecruitmentRequisitions", "RequisitionId", id);

        var approved = await WithDbAsync(host, db => db.RecruitmentRequisitions.AsNoTracking().SingleAsync(r => r.Id == id));
        Assert.Equal(RequisitionStatus.Approved, approved.Status);
        Assert.Equal(WorkflowStatus.Completed, await InstanceStatusAsync(host, approved.WorkflowInstanceId));
    }

    [SkippableFact]
    public async Task Offer_submit_starts_the_workflow_and_two_approvals_issue_it()
    {
        var host = await HostAsync();
        var seed = await SeedAsync(host, RequisitionStatus.Published, withDraftOffer: true);
        var id = seed.OfferId!.Value;

        await PostAsync(host, Submitter, [HrManagementRoles.HrCore],
            $"/OfferLetters/Details/{id:D}", $"/OfferLetters/SubmitOffer/{id:D}", []);
        var pending = await WithDbAsync(host, db => db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == id));
        Assert.Equal(OfferStatus.PendingApproval, pending.Status);
        Assert.NotNull(pending.WorkflowInstanceId);

        await ApproveAsync(host, StepOneApprover, [HrManagementRoles.HrCore], "OfferLetters", "OfferId", id);
        await ApproveAsync(host, ManagementApprover, [HrManagementRoles.HrManagementApprover], "OfferLetters", "OfferId", id);

        var issued = await WithDbAsync(host, db => db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == id));
        Assert.Equal(OfferStatus.Issued, issued.Status);
        Assert.NotNull(issued.IssuedDate);
    }

    [SkippableFact]
    public async Task Committee_submit_starts_the_workflow_and_three_approvals_approve_the_session()
    {
        var host = await HostAsync();
        var seed = await SeedAsync(host, RequisitionStatus.Published, withReadyCommittee: true);
        var id = seed.SessionId!.Value;

        await PostAsync(host, Submitter, [HrManagementRoles.HrCore],
            $"/SelectionCommittees/Details/{id:D}", $"/SelectionCommittees/Submit/{id:D}", []);
        var submitted = await WithDbAsync(host, db => db.SelectionCommitteeSessions.AsNoTracking().SingleAsync(s => s.Id == id));
        Assert.True(submitted.IsLocked);
        Assert.NotNull(submitted.WorkflowInstanceId);

        await ApproveAsync(host, StepOneApprover, [HrManagementRoles.HrCore], "SelectionCommittees", "SessionId", id);
        await ApproveAsync(host, QuorumApprover, [HrManagementRoles.HrCore], "SelectionCommittees", "SessionId", id);
        await ApproveAsync(host, ManagementApprover, [HrManagementRoles.HrManagementApprover], "SelectionCommittees", "SessionId", id);

        var approved = await WithDbAsync(host, db => db.SelectionCommitteeSessions.AsNoTracking().SingleAsync(s => s.Id == id));
        Assert.NotNull(approved.ApprovedAt);
        Assert.Equal(WorkflowStatus.Completed, await InstanceStatusAsync(host, approved.WorkflowInstanceId));
    }

    [SkippableFact]
    public async Task Establishment_change_waits_for_two_approvals_and_is_applied_only_then()
    {
        var host = await HostAsync();
        var seed = await SeedAsync(host, RequisitionStatus.Draft);
        var id = await WithDbAsync(host, async db =>
        {
            var request = new EstablishmentChangeRequest
            {
                TenantId = TenantDefaults.DefaultTenantId,
                RequestNumber = $"ECR-25-{Guid.NewGuid():N}"[..16],
                ChangeType = EstablishmentChangeType.FreezeFundedPosition,
                Status = EstablishmentChangeStatus.Draft,
                TargetEntityId = seed.PositionId,
                PayloadJson = JsonSerializer.Serialize(new FreezeFundedPositionPayload
                {
                    FundedPositionId = seed.PositionId,
                    FreezeReason = "إعادة هيكلة"
                }),
                RequestedAt = DateTime.UtcNow
            };
            db.EstablishmentChangeRequests.Add(request);
            await db.SaveChangesAsync();
            return request.Id;
        });

        await PostAsync(host, Submitter, [HrManagementRoles.HrCore],
            $"/EstablishmentChanges/Details/{id:D}", $"/EstablishmentChanges/Submit/{id:D}",
            [new("LegalSourceId", Syr001LegislationIds.Law50_2004), new("DecisionReference", "DEC-25")]);
        var pending = await WithDbAsync(host, db => db.EstablishmentChangeRequests.AsNoTracking().SingleAsync(r => r.Id == id));
        Assert.Equal(EstablishmentChangeStatus.PendingApproval, pending.Status);
        Assert.NotNull(pending.WorkflowInstanceId);
        Assert.Equal(FundedPositionStatus.Vacant, await PositionStatusAsync(host, seed.PositionId));

        await ApproveAsync(host, StepOneApprover, [HrManagementRoles.HrCore], "EstablishmentChanges", "ChangeRequestId", id);
        Assert.Equal(FundedPositionStatus.Vacant, await PositionStatusAsync(host, seed.PositionId));
        await ApproveAsync(host, ManagementApprover, [HrManagementRoles.HrManagementApprover], "EstablishmentChanges", "ChangeRequestId", id);

        var applied = await WithDbAsync(host, db => db.EstablishmentChangeRequests.AsNoTracking().SingleAsync(r => r.Id == id));
        Assert.Equal(EstablishmentChangeStatus.Applied, applied.Status);
        Assert.Equal(FundedPositionStatus.Frozen, await PositionStatusAsync(host, seed.PositionId));
    }

    [SkippableFact]
    public async Task Repair_endpoint_is_admin_only_dry_run_lists_and_apply_starts_the_stuck_requisition()
    {
        var host = await HostAsync();
        var seed = await SeedAsync(host, RequisitionStatus.PendingApproval);
        const string url = "/api/recruitment/admin/repair-approvals";

        using (var hrCore = CreateClient(host, Submitter, [HrManagementRoles.HrCore]))
            Assert.Equal(HttpStatusCode.Forbidden, (await hrCore.PostAsync($"{url}?dryRun=true", null)).StatusCode);

        using var admin = CreateClient(host, StepOneApprover, [HrManagementRoles.Admin]);
        var dry = await admin.PostAsync($"{url}?dryRun=true", null);
        Assert.Equal(HttpStatusCode.OK, dry.StatusCode);
        Assert.Contains(seed.RequisitionId.ToString("D"), await dry.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(await WithDbAsync(host, db => db.RecruitmentRequisitions.AsNoTracking()
            .Where(r => r.Id == seed.RequisitionId).Select(r => r.WorkflowInstanceId).SingleAsync()));

        var apply = await admin.PostAsync($"{url}?dryRun=false", null);
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        Assert.NotNull(await WithDbAsync(host, db => db.RecruitmentRequisitions.AsNoTracking()
            .Where(r => r.Id == seed.RequisitionId).Select(r => r.WorkflowInstanceId).SingleAsync()));
        var again = await (await admin.PostAsync($"{url}?dryRun=true", null)).Content.ReadAsStringAsync();
        Assert.DoesNotContain(seed.RequisitionId.ToString("D"), again, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- host

    private async Task<WebApplicationFactory<HRCoreWebProgram>> HostAsync()
    {
        Skip.IfNot(postgres.Available, postgres.UnavailableReason);
        if (_host is not null)
            return _host;

        var connectionString = postgres.ConnectionString;
        _host = root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recruitment:Jobs:IntakeCloseEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:OfferExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:ReserveExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateNotificationsEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateRetentionEnabled", "false");
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:HRCoreDb", connectionString);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsAtStartup"] = "false",
                    ["ConnectionStrings:HRCoreDb"] = connectionString,
                    ["HrAuth:BehindGateway"] = "true",
                    ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                    ["HrGateway:InternalBaseUrl"] = "http://localhost:5200",
                    // Production values (appsettings.Production.json, PLAN-25 decision 10).
                    ["NationalScope:IsLabScale"] = "false",
                    ["PolicyEngine:DefaultMode"] = null,
                    ["PolicyEngine:Core.Recruitment"] = "Shadow",
                    ["Recruitment:Approvals:AlwaysStartWorkflow"] = "true",
                    ["Recruitment:Approvals:FailIfDefinitionMissing"] = "true",
                    ["Recruitment:CommitteeApprovalDefault"] = "true",
                    ["Workflow:AdminBypass"] = "false"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddIntegrationTestAuthentication();
            });
        });

        await WithDbAsync(_host, async db =>
        {
            await LegislationRegisterSeeder.SeedAsync(db);
            await WorkflowPackSeeder.SeedAsync(db);
            await PolicyEngineDomainSeeds.SeedAllDomainsAsync(db);
            await PolicyEngineDomainSeeds.SeedIncrementalRulesAsync(db);
            return true;
        });
        return _host;
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<HRCoreWebProgram> host,
        Guid userId,
        IReadOnlyCollection<string> roles,
        Guid? employeeId = null)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, TenantDefaults.DefaultTenantId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        if (employeeId is Guid employee)
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmployeeIdHeader, employee.ToString("D"));
        return client;
    }

    private static Task ApproveAsync(
        WebApplicationFactory<HRCoreWebProgram> host,
        Guid userId,
        IReadOnlyCollection<string> roles,
        string controller,
        string idField,
        Guid id) =>
        PostAsync(host, userId, roles, $"/{controller}/Details/{id:D}", $"/{controller}/ApproveWorkflow/{id:D}",
            [new(idField, id.ToString("D")), new("Comments", "اعتماد")],
            expectSuccess: true);

    /// <summary>GETs the page for a fresh antiforgery token, posts the form, and checks the TempData outcome.</summary>
    private static async Task PostAsync(
        WebApplicationFactory<HRCoreWebProgram> host,
        Guid userId,
        IReadOnlyCollection<string> roles,
        string pageUrl,
        string actionUrl,
        List<KeyValuePair<string, string>> fields,
        bool expectSuccess = true)
    {
        using var client = CreateClient(host, userId, roles);
        var page = await client.GetAsync(pageUrl);
        var raw = await page.Content.ReadAsStringAsync();
        Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {pageUrl} returned {(int)page.StatusCode}: {Truncate(raw)}");
        var match = AntiforgeryTokenPattern.Match(raw);
        Assert.True(match.Success, $"No antiforgery token on {pageUrl}.");

        fields.Add(new("__RequestVerificationToken", WebUtility.HtmlDecode(match.Groups[1].Value)));
        using var content = new FormUrlEncodedContent(fields);
        var response = await client.PostAsync(actionUrl, content);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Redirect,
            $"POST {actionUrl} returned {(int)response.StatusCode}: {Truncate(body)}");

        if (!expectSuccess)
            return;
        var after = WebUtility.HtmlDecode(await (await client.GetAsync(pageUrl)).Content.ReadAsStringAsync());
        var error = Regex.Match(after, "alert-danger[^>]*>(.*?)</div>", RegexOptions.Singleline);
        Assert.False(error.Success, $"POST {actionUrl} reported: {(error.Success ? error.Groups[1].Value.Trim() : "")}");
    }

    private static string Truncate(string value) => value.Length <= 800 ? value : value[..800];

    // ---------------------------------------------------------------- data

    private sealed record SeedIds(Guid RequisitionId, Guid PositionId, Guid? OfferId, Guid? SessionId);

    private static Task<SeedIds> SeedAsync(
        WebApplicationFactory<HRCoreWebProgram> host,
        RequisitionStatus requisitionStatus,
        bool withDraftOffer = false,
        bool withReadyCommittee = false) =>
        WithDbAsync(host, async db =>
        {
            var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var org = new OrgUnit
            {
                TenantId = TenantDefaults.DefaultTenantId,
                Code = $"OU-P25-{suffix}",
                NameAr = "وحدة PLAN-25",
                NameEn = "PLAN-25 unit"
            };
            org.MaterializedPath = $"/{org.Id:D}/";
            db.OrgUnits.Add(org);
            var position = new FundedPosition
            {
                TenantId = TenantDefaults.DefaultTenantId,
                PositionCode = $"VAC-P25-{suffix}",
                GradeCode = "G5",
                BudgetYear = 2026,
                Status = FundedPositionStatus.Vacant,
                OrgUnitId = org.Id
            };
            db.FundedPositions.Add(position);
            var requisition = new RecruitmentRequisition
            {
                TenantId = TenantDefaults.DefaultTenantId,
                RequisitionNumber = $"REQ-P25-{suffix}",
                FundedPositionId = position.Id,
                JobTitle = "محلل نظم",
                Status = requisitionStatus,
                RequestedDate = DateTime.UtcNow,
                VacancyCount = 1,
                LegalSourceId = Syr001LegislationIds.Law50_2004,
                RequiresCommitteeApproval = withReadyCommittee
            };
            db.RecruitmentRequisitions.Add(requisition);
            await db.SaveChangesAsync();

            Guid? offerId = withDraftOffer ? await SeedDraftOfferAsync(db, requisition, position, suffix) : null;
            Guid? sessionId = withReadyCommittee ? await SeedReadyCommitteeAsync(db, requisition, suffix) : null;
            return new SeedIds(requisition.Id, position.Id, offerId, sessionId);
        });

    private static async Task<Guid> SeedDraftOfferAsync(
        HRCoreDbContext db,
        RecruitmentRequisition requisition,
        FundedPosition position,
        string suffix)
    {
        var app = new RecruitmentApplication
        {
            TenantId = TenantDefaults.DefaultTenantId,
            ApplicantFullName = "مرشح أول",
            RequisitionId = requisition.Id,
            AppliedPosition = requisition.JobTitle,
            AppliedAt = DateTime.UtcNow.AddDays(-10),
            Status = RecruitmentApplicationStatus.OfferExtended,
            Rank = 1
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();

        var offer = new OfferLetter
        {
            TenantId = TenantDefaults.DefaultTenantId,
            RecruitmentApplicationId = app.Id,
            FundedPositionId = position.Id,
            OfferNumber = $"OFF-P25-{suffix}",
            Status = OfferStatus.Draft,
            OfferedSalary = 500000m,
            GradeCode = position.GradeCode,
            ProposedStartDate = DateTime.UtcNow.Date.AddDays(30),
            ValidityDays = 14,
            LegalSourceId = Syr001LegislationIds.Law50_2004,
            GradeMinSalary = 400000m,
            GradeMaxSalary = 600000m,
            SalaryCheckedAt = DateTime.UtcNow
        };
        db.OfferLetters.Add(offer);
        app.ActiveOfferLetter = offer;
        await db.SaveChangesAsync();
        return offer.Id;
    }

    /// <summary>Three members (the first is the chair), two assessed candidates, unanimous votes and decisions.</summary>
    private static async Task<Guid> SeedReadyCommitteeAsync(HRCoreDbContext db, RecruitmentRequisition requisition, string suffix)
    {
        var candidates = new List<RecruitmentApplication>();
        decimal[] scores = [90m, 80m];
        for (var i = 0; i < scores.Length; i++)
        {
            var app = new RecruitmentApplication
            {
                TenantId = TenantDefaults.DefaultTenantId,
                ApplicantFullName = string.Create(CultureInfo.InvariantCulture, $"مرشح {i + 1}"),
                RequisitionId = requisition.Id,
                AppliedPosition = requisition.JobTitle,
                AppliedAt = DateTime.UtcNow.AddDays(-30 + i),
                Status = RecruitmentApplicationStatus.Assessed,
                AssessmentScore = scores[i],
                ConflictOfInterestDeclared = true
            };
            db.RecruitmentApplications.Add(app);
            candidates.Add(app);
        }

        await db.SaveChangesAsync();

        var service = new SelectionCommitteeService(db);
        var (session, createError) = await service.CreateSessionAsync(
            requisition.Id, DateTime.UtcNow, null, null, CommitteePurpose.Ranking);
        Assert.True(session is not null, createError);

        for (var i = 0; i < 3; i++)
        {
            var employee = new Employee
            {
                TenantId = TenantDefaults.DefaultTenantId,
                FullName = string.Create(CultureInfo.InvariantCulture, $"عضو لجنة {i + 1}"),
                NationalId = string.Create(CultureInfo.InvariantCulture, $"P25{suffix}{i:D2}")
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();

            var (added, addError) = await service.AddMemberAsync(
                session!.Id,
                employee.Id,
                i == 0 ? SelectionCommitteeMember.ChairRole : SelectionCommitteeMember.MemberRole,
                hasConflictOfInterest: false);
            Assert.True(added, addError);
        }

        var members = await db.SelectionCommitteeMembers.AsNoTracking().Where(m => m.SessionId == session!.Id).ToListAsync();
        var ranks = SelectionCommitteeRules.SuggestedRanks(candidates);
        foreach (var app in candidates)
        {
            foreach (var member in members)
            {
                var (voted, voteError) = await service.RecordVoteAsync(session!.Id, member.Id, app.Id, CommitteeVoteValue.Agree, null);
                Assert.True(voted, voteError);
            }

            var recommendation = ranks[app.Id] == candidates.Count ? CommitteeRecommendation.Reserve : CommitteeRecommendation.Recommend;
            var (decided, decisionError) = await service.UpsertDecisionAsync(
                session!.Id, app.Id, recommendation, ranks[app.Id], "توصية اللجنة");
            Assert.True(decided, decisionError);
        }

        return session!.Id;
    }

    private static async Task<T> WithDbAsync<T>(
        WebApplicationFactory<HRCoreWebProgram> host,
        Func<HRCoreDbContext, Task<T>> query)
    {
        await using var scope = host.Services.CreateAsyncScope();
        TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        try
        {
            return await query(scope.ServiceProvider.GetRequiredService<HRCoreDbContext>());
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private static Task<WorkflowStatus?> InstanceStatusAsync(WebApplicationFactory<HRCoreWebProgram> host, Guid? instanceId) =>
        WithDbAsync(host, db => db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Id == instanceId)
            .Select(i => (WorkflowStatus?)i.Status)
            .SingleOrDefaultAsync());

    private static Task<FundedPositionStatus> PositionStatusAsync(WebApplicationFactory<HRCoreWebProgram> host, Guid positionId) =>
        WithDbAsync(host, db => db.FundedPositions.AsNoTracking().Where(p => p.Id == positionId).Select(p => p.Status).SingleAsync());

    public void Dispose() => _host?.Dispose();

    /// <summary>One migrated PostgreSQL (Testcontainers) for the class; skipped when Docker is unavailable.</summary>
    public sealed class PostgresFixture : IAsyncLifetime
    {
        private PostgreSqlContainer? _postgres;

        public bool Available { get; private set; }
        public string? UnavailableReason { get; private set; }
        public string ConnectionString => _postgres!.GetConnectionString();

        public async Task InitializeAsync()
        {
            try
            {
                _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
                await _postgres.StartAsync();
                await using var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>()
                    .UseNpgsql(ConnectionString, npgsql => npgsql.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name))
                    .Options);
                await db.Database.MigrateAsync();
                Available = true;
            }
            catch (Exception ex)
            {
                UnavailableReason = $"Docker/PostgreSQL unavailable: {ex.GetType().Name}: {ex.Message}";
            }
        }

        public async Task DisposeAsync()
        {
            if (_postgres is not null)
                await _postgres.DisposeAsync();
        }
    }
}
