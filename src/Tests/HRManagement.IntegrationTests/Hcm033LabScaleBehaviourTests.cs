using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace HRManagement.IntegrationTests;

/// <summary>
/// HCM-033 / PLAN-25 (gap <b>G-02</b>, closed by PLAN-25 decision D-1): against the real HR.Core host with the policy
/// engine <c>Off</c>, recruitment approvals start whether the deployment is production-like
/// (<c>NationalScope:IsLabScale=false</c>) or the acceptance/lab host (<c>IsLabScale=true</c>). PLAN-33 wrote these tests to
/// pin the defect (non-lab: PendingApproval without a workflow, a silent committee lock); PLAN-25 flipped the non-lab
/// assertions and added the restart path for rows left stuck by earlier builds. Evidence for <b>AC-HCM-21d</b> and
/// <b>AC-HCM-22e</b>; the full Production-like chain is <c>Hcm025ProductionApprovalIntegrationTests</c>.
/// Driven over HTTP (MVC form posts with the antiforgery token read from the rendered page) with the in-process
/// <see cref="WebApplicationFactory{TEntryPoint}"/>, a per-host SQLite file and <c>PolicyEngine:DefaultMode=Off</c>.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Hcm033LabScaleBehaviourTests : IClassFixture<WebApplicationFactory<HRCoreWebProgram>>, IDisposable
{
    private const string RequisitionWorkflowCode = "RECRUITMENT-REQUISITION-APPROVAL";
    private const string OfferWorkflowCode = "RECRUITMENT-OFFER-APPROVAL";
    private const string CommitteeWorkflowCode = "RECRUITMENT-SELECTION-COMMITTEE";

    private const string AnyMissingWorkflowWarning = "لم يُنشأ مسار اعتماد";

    /// <summary>Fixed actor so the antiforgery token (bound to the user's <c>sub</c>) validates across requests.</summary>
    private static readonly Guid ActorUserId = Guid.Parse("33330000-0000-4000-8000-0000000000a1");

    private static readonly Regex AntiforgeryTokenPattern = new(
        "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly WebApplicationFactory<HRCoreWebProgram> _root;
    private readonly List<WebApplicationFactory<HRCoreWebProgram>> _hosts = [];
    private readonly List<string> _dbPaths = [];

    public Hcm033LabScaleBehaviourTests(WebApplicationFactory<HRCoreWebProgram> root) => _root = root;

    /// <summary>
    /// G-02 — closed by PLAN-25 (AC-HCM-21d, non-lab case): with the policy engine Off and <c>IsLabScale=false</c> the
    /// requisition submit now starts <c>RECRUITMENT-REQUISITION-APPROVAL</c> (before PLAN-25 it stayed PendingApproval
    /// without a workflow forever).
    /// </summary>
    [Fact]
    public async Task Non_lab_off_requisition_submit_now_starts_the_workflow()
    {
        var host = CreateHost(labScale: false);
        var seed = await SeedAsync(host, RequisitionStatus.Draft);
        using var client = CreateClient(host);

        var submit = await PostFormAsync(client, $"/RecruitmentRequisitions/Details/{seed.RequisitionId:D}",
            $"/RecruitmentRequisitions/Submit/{seed.RequisitionId:D}", []);
        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);

        var req = await LoadRequisitionAsync(host, seed.RequisitionId);
        Assert.Equal(RequisitionStatus.PendingApproval, req.Status);
        Assert.NotNull(req.WorkflowInstanceId);
        Assert.Equal(RequisitionWorkflowCode, await GetWorkflowCodeAsync(host, req.WorkflowInstanceId));
        Assert.Equal(1, await CountWorkflowInstancesAsync(host, "RecruitmentRequisition", seed.RequisitionId));

        var details = await GetHtmlAsync(client, $"/RecruitmentRequisitions/Details/{seed.RequisitionId:D}");
        Assert.DoesNotContain(AnyMissingWorkflowWarning, details, StringComparison.Ordinal);
    }

    /// <summary>
    /// PLAN-25 (G-02 repair): a requisition left PendingApproval without a workflow by a pre-PLAN-25 build shows the
    /// restart warning and button; the HrCoreWrite POST starts its workflow and the status stays PendingApproval.
    /// </summary>
    [Fact]
    public async Task Stuck_requisition_shows_restart_button_and_restart_starts_the_workflow()
    {
        var host = CreateHost(labScale: false);
        var seed = await SeedAsync(host, RequisitionStatus.PendingApproval);
        using var client = CreateClient(host);

        var details = await GetHtmlAsync(client, $"/RecruitmentRequisitions/Details/{seed.RequisitionId:D}");
        Assert.Contains(ApprovalRestartRules.StuckWarning, details, StringComparison.Ordinal);
        Assert.Contains(ApprovalRestartRules.RestartButtonLabel, details, StringComparison.Ordinal);
        Assert.DoesNotContain("IsLabScale", details, StringComparison.Ordinal);

        var restart = await PostFormAsync(client, $"/RecruitmentRequisitions/Details/{seed.RequisitionId:D}",
            $"/RecruitmentRequisitions/RestartApproval/{seed.RequisitionId:D}", []);
        Assert.Equal(HttpStatusCode.Redirect, restart.StatusCode);

        var req = await LoadRequisitionAsync(host, seed.RequisitionId);
        Assert.Equal(RequisitionStatus.PendingApproval, req.Status);
        Assert.Equal(RequisitionWorkflowCode, await GetWorkflowCodeAsync(host, req.WorkflowInstanceId));
        var after = await GetHtmlAsync(client, $"/RecruitmentRequisitions/Details/{seed.RequisitionId:D}");
        Assert.DoesNotContain(ApprovalRestartRules.StuckWarning, after, StringComparison.Ordinal);
    }

    /// <summary>
    /// G-02 (open, PLAN-25) — AC-HCM-21d evidence, lab counterpart: same host and policy mode Off but
    /// <c>IsLabScale=true</c>; the submit starts <c>RECRUITMENT-REQUISITION-APPROVAL</c> and no warning is shown.
    /// This is the only reason the acceptance (lab) environment sees an approval path today.
    /// </summary>
    [Fact]
    public async Task Lab_off_requisition_submit_starts_the_workflow()
    {
        var host = CreateHost(labScale: true);
        var seed = await SeedAsync(host, RequisitionStatus.Draft);
        using var client = CreateClient(host);

        var submit = await PostFormAsync(client, $"/RecruitmentRequisitions/Details/{seed.RequisitionId:D}",
            $"/RecruitmentRequisitions/Submit/{seed.RequisitionId:D}", []);
        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);

        var req = await LoadRequisitionAsync(host, seed.RequisitionId);
        Assert.Equal(RequisitionStatus.PendingApproval, req.Status);
        Assert.NotNull(req.WorkflowInstanceId);
        Assert.Equal(RequisitionWorkflowCode, await GetWorkflowCodeAsync(host, req.WorkflowInstanceId));

        var details = await GetHtmlAsync(client, $"/RecruitmentRequisitions/Details/{seed.RequisitionId:D}");
        Assert.DoesNotContain(AnyMissingWorkflowWarning, details, StringComparison.Ordinal);
    }

    /// <summary>
    /// G-02 — closed by PLAN-25 (AC-HCM-22e, non-lab case): with the policy engine Off and <c>IsLabScale=false</c> the
    /// offer submit now starts <c>RECRUITMENT-OFFER-APPROVAL</c>; no missing-workflow warning is shown.
    /// </summary>
    [Fact]
    public async Task Non_lab_off_offer_submit_now_starts_the_workflow()
    {
        var host = CreateHost(labScale: false);
        var seed = await SeedAsync(host, RequisitionStatus.Published, withDraftOffer: true);
        var offerId = seed.OfferId!.Value;
        using var client = CreateClient(host);

        var submit = await PostFormAsync(client, $"/OfferLetters/Details/{offerId:D}",
            $"/OfferLetters/SubmitOffer/{offerId:D}", []);
        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);

        var offer = await LoadOfferAsync(host, offerId);
        Assert.Equal(OfferStatus.PendingApproval, offer.Status);
        Assert.NotNull(offer.WorkflowInstanceId);
        Assert.Equal(OfferWorkflowCode, await GetWorkflowCodeAsync(host, offer.WorkflowInstanceId));
        Assert.Equal(1, await CountWorkflowInstancesAsync(host, "OfferLetter", offerId));

        var details = await GetHtmlAsync(client, $"/OfferLetters/Details/{offerId:D}");
        Assert.DoesNotContain(AnyMissingWorkflowWarning, details, StringComparison.Ordinal);
    }

    /// <summary>
    /// G-02 (open, PLAN-25) — AC-HCM-22e evidence, lab counterpart: with <c>IsLabScale=true</c> the same submit starts
    /// <c>RECRUITMENT-OFFER-APPROVAL</c> and the Details page shows no missing-workflow warning.
    /// </summary>
    [Fact]
    public async Task Lab_off_offer_submit_starts_the_workflow()
    {
        var host = CreateHost(labScale: true);
        var seed = await SeedAsync(host, RequisitionStatus.Published, withDraftOffer: true);
        var offerId = seed.OfferId!.Value;
        using var client = CreateClient(host);

        var submit = await PostFormAsync(client, $"/OfferLetters/Details/{offerId:D}",
            $"/OfferLetters/SubmitOffer/{offerId:D}", []);
        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);

        var offer = await LoadOfferAsync(host, offerId);
        Assert.Equal(OfferStatus.PendingApproval, offer.Status);
        Assert.NotNull(offer.WorkflowInstanceId);
        Assert.Equal(OfferWorkflowCode, await GetWorkflowCodeAsync(host, offer.WorkflowInstanceId));

        var details = await GetHtmlAsync(client, $"/OfferLetters/Details/{offerId:D}");
        Assert.DoesNotContain(AnyMissingWorkflowWarning, details, StringComparison.Ordinal);
    }

    /// <summary>
    /// G-02 — closed by PLAN-25 (AC-HCM-22e, non-lab case): with the policy engine Off and <c>IsLabScale=false</c> the
    /// committee submit now locks the session together with <c>RECRUITMENT-SELECTION-COMMITTEE</c> (before PLAN-25 it
    /// locked silently without any workflow).
    /// </summary>
    [Fact]
    public async Task Non_lab_off_committee_submit_now_starts_the_workflow()
    {
        var host = CreateHost(labScale: false);
        var seed = await SeedAsync(host, RequisitionStatus.Published, withReadyCommittee: true);
        var sessionId = seed.SessionId!.Value;
        using var client = CreateClient(host);

        var submit = await PostFormAsync(client, $"/SelectionCommittees/Details/{sessionId:D}",
            $"/SelectionCommittees/Submit/{sessionId:D}", []);
        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);

        var session = await LoadSessionAsync(host, sessionId);
        Assert.NotNull(session.SubmittedAt);
        Assert.True(session.IsLocked);
        Assert.NotNull(session.WorkflowInstanceId);
        Assert.Equal(CommitteeWorkflowCode, await GetWorkflowCodeAsync(host, session.WorkflowInstanceId));
        Assert.Equal(1, await CountWorkflowInstancesAsync(host, "SelectionCommitteeSession", sessionId));
    }

    /// <summary>
    /// G-02 (open, PLAN-25) — AC-HCM-22e evidence, lab counterpart: with <c>IsLabScale=true</c> the committee submit
    /// starts <c>RECRUITMENT-SELECTION-COMMITTEE</c>.
    /// </summary>
    [Fact]
    public async Task Lab_off_committee_submit_starts_the_workflow()
    {
        var host = CreateHost(labScale: true);
        var seed = await SeedAsync(host, RequisitionStatus.Published, withReadyCommittee: true);
        var sessionId = seed.SessionId!.Value;
        using var client = CreateClient(host);

        var submit = await PostFormAsync(client, $"/SelectionCommittees/Details/{sessionId:D}",
            $"/SelectionCommittees/Submit/{sessionId:D}", []);
        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);

        var session = await LoadSessionAsync(host, sessionId);
        Assert.True(session.IsLocked);
        Assert.NotNull(session.WorkflowInstanceId);
        Assert.Equal(CommitteeWorkflowCode, await GetWorkflowCodeAsync(host, session.WorkflowInstanceId));
    }

    // ---------------------------------------------------------------- host

    private WebApplicationFactory<HRCoreWebProgram> CreateHost(bool labScale)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"hr-hcm033-g02-{Guid.NewGuid():N}.db");
        _dbPaths.Add(dbPath);
        var host = _root.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Recruitment:Jobs:IntakeCloseEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:OfferExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:ReserveExpiryEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateNotificationsEnabled", "false");
            builder.UseSetting("Recruitment:Jobs:CandidateRetentionEnabled", "false");
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:HRCoreDb", $"Data Source={dbPath}");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:RunMigrationsAtStartup"] = "false",
                    ["ConnectionStrings:HRCoreDb"] = $"Data Source={dbPath}",
                    ["HrAuth:BehindGateway"] = "true",
                    ["Keycloak:Authority"] = "http://localhost:8080/realms/hrmanagement",
                    ["HrGateway:InternalBaseUrl"] = "http://localhost:5200",
                    // G-02: the two switches under test.
                    ["PolicyEngine:DefaultMode"] = "Off",
                    ["PolicyEngine:Core.Recruitment"] = "Off",
                    ["NationalScope:IsLabScale"] = labScale ? "true" : "false"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddIntegrationTestAuthentication();
            });
        });
        _hosts.Add(host);
        return host;
    }

    private static HttpClient CreateClient(WebApplicationFactory<HRCoreWebProgram> host)
    {
        // HandleCookies (default) keeps the antiforgery and TempData cookies between requests.
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, TenantDefaults.DefaultTenantId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, ActorUserId.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader,
            $"{HrManagementRoles.Admin},{HrManagementRoles.HrCore}");
        return client;
    }

    private static async Task<string> GetHtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"GET {url} returned {(int)response.StatusCode}: {Truncate(body)}");
        // Razor HTML-encodes Arabic text by default; compare on the decoded page.
        return WebUtility.HtmlDecode(body);
    }

    /// <summary>GETs <paramref name="pageUrl"/> for a fresh antiforgery token, then posts the MVC form.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string pageUrl,
        string actionUrl,
        List<KeyValuePair<string, string>> fields)
    {
        var page = await client.GetAsync(pageUrl);
        var raw = await page.Content.ReadAsStringAsync();
        Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {pageUrl} returned {(int)page.StatusCode}: {Truncate(raw)}");
        var match = AntiforgeryTokenPattern.Match(raw);
        Assert.True(match.Success, $"No antiforgery token on {pageUrl}.");

        fields.Add(new("__RequestVerificationToken", WebUtility.HtmlDecode(match.Groups[1].Value)));
        using var content = new FormUrlEncodedContent(fields);
        var response = await client.PostAsync(actionUrl, content);
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"POST {actionUrl} returned {(int)response.StatusCode}: {Truncate(body)}");
        }

        return response;
    }

    private static string Truncate(string value) => value.Length <= 800 ? value : value[..800];

    // ---------------------------------------------------------------- data

    private sealed record SeedIds(Guid RequisitionId, Guid? OfferId, Guid? SessionId);

    /// <summary>
    /// Default-tenant org unit + vacant funded position + requisition (legal source Law 50/2004), the legislation register
    /// and the national workflow pack (so the lab host can resolve the three recruitment workflow codes).
    /// </summary>
    private static async Task<SeedIds> SeedAsync(
        WebApplicationFactory<HRCoreWebProgram> host,
        RequisitionStatus requisitionStatus,
        bool withDraftOffer = false,
        bool withReadyCommittee = false)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
        await db.Database.EnsureCreatedAsync();

        TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        try
        {
            await LegislationRegisterSeeder.SeedAsync(db);
            await WorkflowPackSeeder.SeedAsync(db);

            var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var org = new OrgUnit
            {
                TenantId = TenantDefaults.DefaultTenantId,
                Code = $"OU-G02-{suffix}",
                NameAr = "وحدة G-02",
                NameEn = "G-02 unit"
            };
            org.MaterializedPath = $"/{org.Id:D}/";
            db.OrgUnits.Add(org);

            var position = new FundedPosition
            {
                TenantId = TenantDefaults.DefaultTenantId,
                PositionCode = $"VAC-G02-{suffix}",
                GradeCode = "G5",
                BudgetYear = 2026,
                Status = FundedPositionStatus.Vacant,
                OrgUnitId = org.Id
            };
            db.FundedPositions.Add(position);

            var requisition = new RecruitmentRequisition
            {
                TenantId = TenantDefaults.DefaultTenantId,
                RequisitionNumber = $"REQ-G02-{suffix}",
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

            Guid? offerId = null;
            if (withDraftOffer)
                offerId = await SeedDraftOfferAsync(db, requisition, position, suffix);

            Guid? sessionId = null;
            if (withReadyCommittee)
                sessionId = await SeedReadyCommitteeAsync(db, requisition, suffix);

            return new SeedIds(requisition.Id, offerId, sessionId);
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    /// <summary>Rank-1 candidate with a non-exception draft offer (grade = position grade, salary verified).</summary>
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
        // Two saves: application -> offer -> ActiveOfferLetterId is a cycle EF cannot order in one batch.
        await db.SaveChangesAsync();

        var offer = new OfferLetter
        {
            TenantId = TenantDefaults.DefaultTenantId,
            RecruitmentApplicationId = app.Id,
            FundedPositionId = position.Id,
            OfferNumber = $"OFF-G02-{suffix}",
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

    /// <summary>
    /// A session ready to submit (same shape as the HCM-028 unit fixtures): three members (the first is the chair),
    /// two assessed candidates, every member agrees, the first recommended at rank 1 and the second kept in reserve.
    /// </summary>
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
                NationalId = string.Create(CultureInfo.InvariantCulture, $"G02{suffix}{i:D2}")
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

        var members = await db.SelectionCommitteeMembers.AsNoTracking()
            .Where(m => m.SessionId == session!.Id)
            .ToListAsync();
        var ranks = SelectionCommitteeRules.SuggestedRanks(candidates);
        foreach (var app in candidates)
        {
            foreach (var member in members)
            {
                var (voted, voteError) = await service.RecordVoteAsync(
                    session!.Id, member.Id, app.Id, CommitteeVoteValue.Agree, null);
                Assert.True(voted, voteError);
            }

            var recommendation = ranks[app.Id] == candidates.Count
                ? CommitteeRecommendation.Reserve
                : CommitteeRecommendation.Recommend;
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

    private static Task<RecruitmentRequisition> LoadRequisitionAsync(WebApplicationFactory<HRCoreWebProgram> host, Guid id) =>
        WithDbAsync(host, db => db.RecruitmentRequisitions.AsNoTracking().SingleAsync(r => r.Id == id));

    private static Task<OfferLetter> LoadOfferAsync(WebApplicationFactory<HRCoreWebProgram> host, Guid id) =>
        WithDbAsync(host, db => db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == id));

    private static Task<SelectionCommitteeSession> LoadSessionAsync(WebApplicationFactory<HRCoreWebProgram> host, Guid id) =>
        WithDbAsync(host, db => db.SelectionCommitteeSessions.AsNoTracking().SingleAsync(s => s.Id == id));

    private static Task<int> CountWorkflowInstancesAsync(WebApplicationFactory<HRCoreWebProgram> host, string entityType, Guid entityId) =>
        WithDbAsync(host, db => db.WorkflowInstances.AsNoTracking()
            .CountAsync(i => i.EntityType == entityType && i.EntityId == entityId));

    private static Task<string?> GetWorkflowCodeAsync(WebApplicationFactory<HRCoreWebProgram> host, Guid? instanceId) =>
        WithDbAsync(host, async db =>
        {
            if (instanceId is not Guid id)
                return null;
            var instance = await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == id);
            return await db.WorkflowDefinitions.AsNoTracking()
                .Where(d => d.Id == instance.DefinitionId)
                .Select(d => (string?)d.Code)
                .SingleAsync();
        });

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            try
            {
                host.Dispose();
            }
            catch
            {
                // best effort
            }
        }

        SqliteConnection.ClearAllPools();
        foreach (var path in _dbPaths)
        {
            try
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch
            {
                // best effort temp cleanup
            }
        }
    }
}
