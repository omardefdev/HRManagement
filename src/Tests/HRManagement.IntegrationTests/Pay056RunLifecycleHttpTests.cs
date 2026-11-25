using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Features.PayrollRuns.Reversal;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.OrgScope;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-56 (P-4): the acts of a payroll run's life over HTTP on Payroll.Web itself (PostgreSQL). One rule for the answers: 400 a
/// missing note or reason or a state the act cannot leave (and a separation-of-duties refusal of a handler), 403 a policy the caller
/// does not hold (and D-PAY-18), 404 another agency's run or request, 409 a request that lost to another (a second pending reversal
/// request, a second open correction, a decided request, a duplicate bank reference). Every new write needs the antiforgery token,
/// a branch-restricted user is refused (the run belongs to the whole agency), and every refusal leaves the run as it was.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay056RunLifecycleHttpTests(PostgreSqlFixture postgres)
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Guid OtherAgency = Guid.Parse("56000000-0000-4000-8000-0000000000b0");
    private static readonly Guid Processor = Guid.Parse("56000000-0000-4000-8000-0000000000e1");
    private static readonly Guid Approver = Guid.Parse("56000000-0000-4000-8000-0000000000e2");
    private static readonly Guid Poster = Guid.Parse("56000000-0000-4000-8000-0000000000e3");
    private static readonly Guid Reverser = Guid.Parse("56000000-0000-4000-8000-0000000000e4");
    private static readonly DateTime PostedAt = new(2028, 2, 2, 8, 0, 0, DateTimeKind.Utc);

    private static readonly string ProcessorRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor}";
    private static readonly string ApproverRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}";
    private static readonly string PosterRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollPoster}";
    private static readonly string ReverserRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollReverser}";

    private const string TokenPath = "/api/payroll/run-lifecycle/antiforgery";

    private static async Task<Dictionary<Type, Guid>> SeedAsync(PayrollDbContext db, Guid unit)
    {
        var employee = Guid.NewGuid();
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            Id = employee,
            TenantId = Tenant,
            EmployeeId = employee,
            BaseSalary = 4500m,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true,
            OrgUnitId = unit,
        });
        await db.SaveChangesAsync();
        return new Dictionary<Type, Guid> { [typeof(EmployeePayProfile)] = employee };
    }

    private Task<Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>> StartAsync() =>
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>.StartAsync(
            postgres, "PayrollDb", PayrollOrgScopeKeys.Registry, ProcessorRoles, ApproverRoles, "/EmployeePayProfiles/Create", SeedAsync,
            settings: new Dictionary<string, string?> { ["Seed:Profile"] = "None" });

    /// <summary>A client of the agency (or of another one), as one person with his roles; restricted to a unit when one is given.</summary>
    private static HttpClient As(
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, Guid user, string roles, Guid? agency = null, Guid? unit = null)
    {
        var client = host.Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, (agency ?? Tenant).ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        if (unit is Guid scope)
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopeOrgUnitHeader, scope.ToString("D"));
        return client;
    }

    private static async Task<HttpClient> WithTokenAsync(HttpClient client, string tokenPath = TokenPath)
    {
        using var issued = await client.GetAsync(tokenPath);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        using var token = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add(
            token.RootElement.GetProperty("headerName").GetString()!, token.RootElement.GetProperty("requestToken").GetString());
        return client;
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('{'))
            return null;
        using var json = JsonDocument.Parse(text);
        return json.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
    }

    /// <summary>A run planted in a state, with one sound line and, when posted, its poster and a batch in <paramref name="batch"/>.</summary>
    private static async Task<Guid> PlantAsync(
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, PayrollRunStatus status, int month, PaymentBatchStatus? batch = null)
    {
        var start = new DateTime(2028, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var posted = status == PayrollRunStatus.Posted;
        var run = new PayrollRun
        {
            TenantId = Tenant,
            PeriodStart = start,
            PeriodEnd = start.AddMonths(1).AddDays(-1),
            RunDate = start.AddMonths(1),
            Status = posted ? PayrollRunStatus.Approved : status,
            TotalNetPay = 900m,
            ResultsFrozen = status is PayrollRunStatus.UnderReview or PayrollRunStatus.Approved or PayrollRunStatus.Posted,
            ValidatedAtCalculate = status != PayrollRunStatus.Draft,
        };
        if (posted)
            run.Post(Poster, PostedAt);
        var line = new SalaryLine { TenantId = Tenant, PayrollRunId = run.Id, EmployeeId = Guid.NewGuid(), GrossPay = 1000m, TotalDeductions = 100m, NetPay = 900m };
        await host.WithDbAsync(async db =>
        {
            db.Add(run);
            db.Add(line);
            foreach (var component in TestDataFactory.ComponentsOf(line))
            {
                component.TenantId = Tenant;
                db.Add(component);
            }

            if (batch is PaymentBatchStatus batchStatus)
            {
                db.Add(new PaymentBatch
                {
                    TenantId = Tenant,
                    PayrollRunId = run.Id,
                    TotalAmount = 900m,
                    Status = batchStatus,
                    BankTransferReference = $"PLANTED-{month:00}",
                });
            }

            return await db.SaveChangesAsync();
        });
        return run.Id;
    }

    private static Task<PayrollRun> RunAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, Guid id) =>
        host.WithDbAsync(db => db.Set<PayrollRun>().AsNoTracking().IgnoreQueryFilters().SingleAsync(r => r.Id == id));

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Every_new_write_needs_the_antiforgery_token()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var underReview = await PlantAsync(host, PayrollRunStatus.UnderReview, 1);
        var posted = await PlantAsync(host, PayrollRunStatus.Posted, 2, PaymentBatchStatus.Pending);
        using var approver = As(host, Approver, ApproverRoles);
        using var processor = As(host, Processor, ProcessorRoles);
        using var reverser = As(host, Reverser, ReverserRoles);

        // No token on the request: each new POST is refused before its action runs, and nothing changes.
        var writes = new (HttpClient Client, string Path, object Body)[]
        {
            (approver, $"/api/payroll/runs/{underReview}/reject", new { notes = "ملاحظة" }),
            (approver, $"/api/payroll/runs/{underReview}/cancel", new { reason = "سبب" }),
            (processor, $"/api/payroll/jobs/{Guid.NewGuid()}/restart", new { }),
            (processor, $"/api/payroll/runs/{posted}/corrections", new { }),
            (reverser, $"/api/payroll/runs/{posted}/reversal-requests", new { reason = "سبب" }),
            (approver, $"/api/payroll/reversal-requests/{Guid.NewGuid()}/decide", new { approve = true }),
        };
        foreach (var (client, path, body) in writes)
        {
            using var response = await client.PostAsJsonAsync(path, body);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{path}: {(int)response.StatusCode}");
        }

        Assert.Equal(PayrollRunStatus.UnderReview, (await RunAsync(host, underReview)).Status);
        Assert.Equal(PayrollRunStatus.Posted, (await RunAsync(host, posted)).Status);
        Assert.Equal(0, await host.WithDbAsync(db => db.Set<PayrollReversalRequest>().IgnoreQueryFilters().CountAsync()));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_rejection_is_the_approvers_needs_its_note_and_reopens_the_run()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var runId = await PlantAsync(host, PayrollRunStatus.UnderReview, 1);
        using var processor = await WithTokenAsync(As(host, Processor, ProcessorRoles));
        using var approver = await WithTokenAsync(As(host, Approver, ApproverRoles));
        var path = $"/api/payroll/runs/{runId}/reject";

        using (var byProcessor = await processor.PostAsJsonAsync(path, new { notes = "ملاحظة" }))
            Assert.Equal(HttpStatusCode.Forbidden, byProcessor.StatusCode);
        using (var noNote = await approver.PostAsJsonAsync(path, new { notes = "  " }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);
            Assert.Equal(PayrollRun.RejectionNoteRequired, await ErrorAsync(noNote));
        }

        Assert.Equal((PayrollRunStatus.UnderReview, 1), ((await RunAsync(host, runId)).Status, (await RunAsync(host, runId)).Revision));

        using (var rejected = await approver.PostAsJsonAsync(path, new { notes = "بدل النقل لم يُحتسب" }))
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var run = await RunAsync(host, runId);
        Assert.Equal((PayrollRunStatus.Draft, 2, false, false), (run.Status, run.Revision, run.ResultsFrozen, run.ValidatedAtCalculate));
        Assert.Equal(0, await host.WithDbAsync(db => db.Set<SalaryLine>().IgnoreQueryFilters().CountAsync(l => l.PayrollRunId == runId)));

        // It is a draft now: a second rejection has no run under review to reject.
        using (var again = await approver.PostAsJsonAsync(path, new { notes = "مرة أخرى" }))
            Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        using (var unknown = await approver.PostAsJsonAsync($"/api/payroll/runs/{Guid.NewGuid()}/reject", new { notes = "ملاحظة" }))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_cancellation_follows_D_PAY_18_and_needs_its_reason()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var approved = await PlantAsync(host, PayrollRunStatus.Approved, 1);
        var calculated = await PlantAsync(host, PayrollRunStatus.Calculated, 2);
        var posted = await PlantAsync(host, PayrollRunStatus.Posted, 3, PaymentBatchStatus.Pending);
        using var processor = await WithTokenAsync(As(host, Processor, ProcessorRoles));
        using var approver = await WithTokenAsync(As(host, Approver, ApproverRoles));
        using var poster = await WithTokenAsync(As(host, Poster, PosterRoles));
        using var moduleOnly = await WithTokenAsync(As(host, Guid.NewGuid(), HrManagementRoles.Payroll));
        static string Cancel(Guid run) => $"/api/payroll/runs/{run}/cancel";
        var reason = new { reason = "أُنشئ التشغيل لفترة خاطئة" };

        // The processor cancelling an approved run: 403, the run as it was (D-PAY-18).
        using (var byProcessor = await processor.PostAsJsonAsync(Cancel(approved), reason))
        {
            Assert.Equal(HttpStatusCode.Forbidden, byProcessor.StatusCode);
            Assert.Equal(RunLifecycleMessages.CancelAfterReviewNeedsApprover, await ErrorAsync(byProcessor));
        }

        // Who holds neither the processing nor the approval role does not reach the act at all.
        foreach (var other in new[] { poster, moduleOnly })
        {
            using var refused = await other.PostAsJsonAsync(Cancel(calculated), reason);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        // The approver alone does not cancel what was not sent for review: that is the processor's.
        using (var byApprover = await approver.PostAsJsonAsync(Cancel(calculated), reason))
            Assert.Equal(HttpStatusCode.Forbidden, byApprover.StatusCode);
        using (var noReason = await approver.PostAsJsonAsync(Cancel(approved), new { reason = "" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
            Assert.Equal(PayrollRun.CancellationReasonRequired, await ErrorAsync(noReason));
        }

        using (var ofPosted = await approver.PostAsJsonAsync(Cancel(posted), reason))
            Assert.Equal(HttpStatusCode.BadRequest, ofPosted.StatusCode);
        Assert.Equal(PayrollRunStatus.Approved, (await RunAsync(host, approved)).Status);
        Assert.Equal(PayrollRunStatus.Calculated, (await RunAsync(host, calculated)).Status);
        Assert.Equal(PayrollRunStatus.Posted, (await RunAsync(host, posted)).Status);

        using (var done = await approver.PostAsJsonAsync(Cancel(approved), reason))
            Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        using (var doneByProcessor = await processor.PostAsJsonAsync(Cancel(calculated), reason))
            Assert.Equal(HttpStatusCode.OK, doneByProcessor.StatusCode);
        var cancelled = await RunAsync(host, approved);
        Assert.Equal((PayrollRunStatus.Cancelled, (Guid?)Approver, "أُنشئ التشغيل لفترة خاطئة"), (cancelled.Status, cancelled.CancelledBy, cancelled.CancellationReason));
        Assert.Equal((PayrollRunStatus.Cancelled, (Guid?)Processor), ((await RunAsync(host, calculated)).Status, (await RunAsync(host, calculated)).CancelledBy));

        // The older cancel-by-job endpoint now needs a reason too, and an unknown job is not found.
        using (var unknownJob = await processor.PostAsJsonAsync($"/api/payroll/jobs/{Guid.NewGuid()}/cancel", reason))
            Assert.Equal(HttpStatusCode.NotFound, unknownJob.StatusCode);
        using (var unknownRun = await approver.PostAsJsonAsync(Cancel(Guid.NewGuid()), reason))
            Assert.Equal(HttpStatusCode.NotFound, unknownRun.StatusCode);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Another_agencys_run_is_not_found_and_a_branch_restricted_user_is_refused()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var underReview = await PlantAsync(host, PayrollRunStatus.UnderReview, 1);
        var posted = await PlantAsync(host, PayrollRunStatus.Posted, 2, PaymentBatchStatus.Pending);
        using var reverser = await WithTokenAsync(As(host, Reverser, ReverserRoles));
        using (var asked = await reverser.PostAsJsonAsync($"/api/payroll/runs/{posted}/reversal-requests", new { reason = "رُحّل لفترة خاطئة" }))
            Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        var requestId = await host.WithDbAsync(db => db.Set<PayrollReversalRequest>().IgnoreQueryFilters().Select(r => r.Id).SingleAsync());

        // The same roles in another agency: the run, the job and the request are not there.
        var everyRole = $"{ProcessorRoles},{HrManagementRoles.PayrollApprover},{HrManagementRoles.PayrollPoster},{HrManagementRoles.PayrollReverser}";
        using var foreigner = await WithTokenAsync(As(host, Guid.NewGuid(), everyRole, agency: OtherAgency));
        var foreign = new (string Path, object Body)[]
        {
            ($"/api/payroll/runs/{underReview}/reject", new { notes = "ملاحظة" }),
            ($"/api/payroll/runs/{underReview}/cancel", new { reason = "سبب" }),
            ($"/api/payroll/runs/{posted}/reversal-requests", new { reason = "سبب" }),
            ($"/api/payroll/reversal-requests/{requestId}/decide", new { approve = true }),
            ($"/api/payroll/runs/{posted}/corrections", new { }),
        };
        foreach (var (path, body) in foreign)
        {
            using var response = await foreigner.PostAsJsonAsync(path, body);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{path}: {(int)response.StatusCode}");
        }

        using (var list = await foreigner.GetAsync("/api/payroll/reversal-requests"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal("[]", (await list.Content.ReadAsStringAsync()).Trim());
        }

        // A user restricted to a branch: a run belongs to the whole agency, so the acts of its life are central.
        // He is refused on every action of the controller, the token's endpoint included. With a token of Payroll taken where a
        // restricted user may take one (every Payroll API write accepts it), the act itself answers 403.
        using var restricted = As(host, Guid.NewGuid(), $"{HrManagementRoles.BranchManager},{everyRole}", unit: host.OwnUnit);
        using (var noToken = await restricted.GetAsync(TokenPath))
            Assert.Equal(HttpStatusCode.Forbidden, noToken.StatusCode);
        await WithTokenAsync(restricted, "/api/payroll/bank-accounts/antiforgery");
        using (var refused = await restricted.PostAsJsonAsync($"/api/payroll/runs/{underReview}/reject", new { notes = "ملاحظة" }))
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using (var refusedCancel = await restricted.PostAsJsonAsync($"/api/payroll/runs/{underReview}/cancel", new { reason = "سبب" }))
            Assert.Equal(HttpStatusCode.Forbidden, refusedCancel.StatusCode);
        using (var refusedList = await restricted.GetAsync("/api/payroll/reversal-requests"))
            Assert.Equal(HttpStatusCode.Forbidden, refusedList.StatusCode);

        Assert.Equal(PayrollRunStatus.UnderReview, (await RunAsync(host, underReview)).Status);
        Assert.Equal(PayrollRunStatus.Posted, (await RunAsync(host, posted)).Status);
        Assert.Equal(PayrollReversalRequestStatus.Pending,
            await host.WithDbAsync(db => db.Set<PayrollReversalRequest>().IgnoreQueryFilters().Select(r => r.Status).SingleAsync()));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_reversal_is_requested_by_a_reverser_and_decided_by_an_approver_who_is_neither_the_requester_nor_the_poster()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var runId = await PlantAsync(host, PayrollRunStatus.Posted, 1, PaymentBatchStatus.Pending);
        using var reverser = await WithTokenAsync(As(host, Reverser, ReverserRoles));
        using var approver = await WithTokenAsync(As(host, Approver, ApproverRoles));
        // The person who posted the run, holding the approval role as well: the handler's rule still refuses him.
        using var posterWhoApproves = await WithTokenAsync(As(host, Poster, $"{PosterRoles},{HrManagementRoles.PayrollApprover}"));
        var ask = $"/api/payroll/runs/{runId}/reversal-requests";

        using (var byApprover = await approver.PostAsJsonAsync(ask, new { reason = "سبب" }))
            Assert.Equal(HttpStatusCode.Forbidden, byApprover.StatusCode);
        using (var noReason = await reverser.PostAsJsonAsync(ask, new { reason = " " }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
            Assert.Equal(PayrollReversalMessages.ReasonRequired, await ErrorAsync(noReason));
        }

        Guid requestId;
        using (var asked = await reverser.PostAsJsonAsync(ask, new { reason = "رُحّل التشغيل قبل اعتماد التسويات" }))
        {
            Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
            requestId = JsonDocument.Parse(await asked.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        }

        using (var second = await reverser.PostAsJsonAsync(ask, new { reason = "طلب ثانٍ" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal(PayrollReversalMessages.PendingRequestExists, await ErrorAsync(second));
        }

        var decide = $"/api/payroll/reversal-requests/{requestId}/decide";
        // The reverser deciding his own request: he does not hold the approval policy.
        using (var byRequester = await reverser.PostAsJsonAsync(decide, new { approve = true }))
            Assert.Equal(HttpStatusCode.Forbidden, byRequester.StatusCode);
        using (var byPoster = await posterWhoApproves.PostAsJsonAsync(decide, new { approve = true }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, byPoster.StatusCode);
            Assert.Equal(PayrollReversalMessages.DeciderIsPoster, await ErrorAsync(byPoster));
        }

        Assert.Equal(PayrollRunStatus.Posted, (await RunAsync(host, runId)).Status);

        // The list, open to the module: the request waits, with its run's period.
        using (var listed = await approver.GetAsync("/api/payroll/reversal-requests?status=Pending"))
        {
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            var rows = JsonDocument.Parse(await listed.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(requestId, Assert.Single(rows.EnumerateArray()).GetProperty("id").GetGuid());
        }

        using (var approved = await approver.PostAsJsonAsync(decide, new { approve = true, notes = "لم تُرسل الدفعة" }))
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(PayrollRunStatus.Reversed, (await RunAsync(host, runId)).Status);
        Assert.Equal(PaymentBatchStatus.Voided,
            await host.WithDbAsync(db => db.Set<PaymentBatch>().IgnoreQueryFilters().Where(b => b.PayrollRunId == runId).Select(b => b.Status).SingleAsync()));
        using (var again = await approver.PostAsJsonAsync(decide, new { approve = false, notes = "بعد القرار" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Equal(PayrollReversalRequest.AlreadyDecided, await ErrorAsync(again));
        }
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_run_whose_batch_was_sent_is_not_reversed_and_the_answer_points_to_a_correction_run()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var runId = await PlantAsync(host, PayrollRunStatus.Posted, 1, PaymentBatchStatus.Submitted);
        using var reverser = await WithTokenAsync(As(host, Reverser, ReverserRoles));
        using var approver = await WithTokenAsync(As(host, Approver, ApproverRoles));
        Guid requestId;
        using (var asked = await reverser.PostAsJsonAsync($"/api/payroll/runs/{runId}/reversal-requests", new { reason = "صُرف بدل لا يُستحق" }))
        {
            Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
            requestId = JsonDocument.Parse(await asked.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        }

        var decide = $"/api/payroll/reversal-requests/{requestId}/decide";
        using (var approved = await approver.PostAsJsonAsync(decide, new { approve = true }))
        {
            // D-PAY-16.
            Assert.Equal(HttpStatusCode.BadRequest, approved.StatusCode);
            Assert.Equal(PayrollReversalMessages.MoneyHasLeft, await ErrorAsync(approved));
        }

        Assert.Equal(PayrollRunStatus.Posted, (await RunAsync(host, runId)).Status);
        Assert.Equal(PaymentBatchStatus.Submitted,
            await host.WithDbAsync(db => db.Set<PaymentBatch>().IgnoreQueryFilters().Where(b => b.PayrollRunId == runId).Select(b => b.Status).SingleAsync()));

        using (var noNote = await approver.PostAsJsonAsync(decide, new { approve = false }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);
            Assert.Equal(PayrollReversalMessages.RejectionNoteRequired, await ErrorAsync(noNote));
        }

        using (var rejected = await approver.PostAsJsonAsync(decide, new { approve = false, notes = "خرج المال: يُصحَّح بتشغيل تصحيح" }))
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(PayrollReversalRequestStatus.Rejected,
            await host.WithDbAsync(db => db.Set<PayrollReversalRequest>().IgnoreQueryFilters().Select(r => r.Status).SingleAsync()));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_correction_is_made_of_targeted_adjustments_and_a_second_open_one_is_a_conflict()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        // A posted run with a pinned framework version: what a correction needs of its source.
        var framework = PayrollRegimeSeed.CreateLaw50Configuration(isActive: true);
        framework.TenantId = Tenant;
        framework.ContentHash = PayrollConfigurationHashService.ComputeContentHash(framework);
        framework.EffectiveFrom = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var source = await PlantAsync(host, PayrollRunStatus.Posted, 1, PaymentBatchStatus.Pending);
        var (employee, _) = host.Records[typeof(EmployeePayProfile)];
        await host.WithDbAsync(async db =>
        {
            db.Add(framework);
            await db.SaveChangesAsync();
            var run = await db.Set<PayrollRun>().IgnoreQueryFilters().SingleAsync(r => r.Id == source);
            run.PayrollConfigurationId = framework.Id;
            run.ConfigContentHash = framework.ContentHash;
            // The source paid this employee: its one line is his.
            (await db.Set<SalaryLine>().IgnoreQueryFilters().SingleAsync(l => l.PayrollRunId == source)).EmployeeId = employee;
            return await db.SaveChangesAsync();
        });
        using var processor = await WithTokenAsync(As(host, Processor, ProcessorRoles));
        using var approver = await WithTokenAsync(As(host, Approver, ApproverRoles));
        var path = $"/api/payroll/runs/{source}/corrections";

        using (var byApprover = await approver.PostAsJsonAsync(path, new { }))
            Assert.Equal(HttpStatusCode.Forbidden, byApprover.StatusCode);
        using (var none = await processor.PostAsJsonAsync(path, new { }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
            Assert.Equal(CorrectionRunMessages.NoTargetedAdjustments, await ErrorAsync(none));
        }

        // The general creation of a run is no second door to a correction (review): 400 with the way to make one, and no run.
        var january = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var general = await processor.PostAsJsonAsync("/api/payroll/runs", new
        {
            periodStart = january,
            periodEnd = january.AddMonths(1).AddDays(-1),
            runDate = january,
            runType = (int)PayrollRunType.Correction,
            sourceRunId = source,
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, general.StatusCode);
            Assert.Equal(CorrectionRunMessages.CreatedFromItsSource, await ErrorAsync(general));
        }

        Assert.Equal(0, await host.WithDbAsync(db => db.Set<PayrollRun>().IgnoreQueryFilters().CountAsync(r => r.RunType == PayrollRunType.Correction)));

        await host.WithDbAsync(async db =>
        {
            db.Add(new PayrollAdjustment { TenantId = Tenant, EmployeeId = employee, Amount = -30_000m, AdjustmentReason = "صُرف بدل لا يُستحق", TargetSourceRunId = source });
            return await db.SaveChangesAsync();
        });
        Guid correctionId;
        using (var created = await processor.PostAsJsonAsync(path, new { }))
        {
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
            correctionId = body.GetProperty("id").GetGuid();
            Assert.Equal(1, body.GetProperty("linesGenerated").GetInt32());
        }

        var correction = await RunAsync(host, correctionId);
        Assert.Equal((PayrollRunType.Correction, PayrollRunStatus.Calculated, true, 0m), (correction.RunType, correction.Status, correction.ValidatedAtCalculate, correction.TotalNetPay));
        Assert.Equal(30_000m, await host.WithDbAsync(db => db.Set<DeductionRecord>().IgnoreQueryFilters()
            .Where(d => d.DeductionType == CorrectionRecovery.DeductionType).Select(d => d.Amount).SingleAsync()));

        await host.WithDbAsync(async db =>
        {
            db.Add(new PayrollAdjustment { TenantId = Tenant, EmployeeId = employee, Amount = 1_000m, AdjustmentReason = "تصحيح آخر", TargetSourceRunId = source });
            return await db.SaveChangesAsync();
        });
        using (var second = await processor.PostAsJsonAsync(path, new { }))
        {
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal(CorrectionRunMessages.OpenCorrectionExists, await ErrorAsync(second));
        }

        Assert.Equal(1, await host.WithDbAsync(db => db.Set<PayrollRun>().IgnoreQueryFilters().CountAsync(r => r.RunType == PayrollRunType.Correction)));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Validate_on_an_approved_run_and_a_restart_of_what_did_not_fail_are_refused_and_change_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var approved = await PlantAsync(host, PayrollRunStatus.Approved, 1);
        var completed = Guid.NewGuid();
        await host.WithDbAsync(async db =>
        {
            db.Add(new PayrollRunJob { Id = completed, TenantId = Tenant, PayrollRunId = approved, Status = PayrollRunJobStatus.Completed, Phase = PayrollRunJobPhase.Validate, ResultsValidated = true });
            return await db.SaveChangesAsync();
        });
        using var processor = await WithTokenAsync(As(host, Processor, ProcessorRoles));

        // The validation used to bring an approved run back to «محسوب».
        using (var validated = await processor.PostAsync($"/api/payroll/runs/{approved}/validate", null))
        {
            Assert.Equal(HttpStatusCode.BadRequest, validated.StatusCode);
            Assert.Contains("لا يمكن التحقق من تشغيل", await ErrorAsync(validated));
        }

        using (var recalculated = await processor.PostAsync($"/api/payroll/runs/{approved}/calculate", null))
            Assert.Equal(HttpStatusCode.BadRequest, recalculated.StatusCode);
        using (var restarted = await processor.PostAsJsonAsync($"/api/payroll/jobs/{completed}/restart", new { }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, restarted.StatusCode);
            Assert.Equal(RestartCalculationHandler.OnlyFailedOrPaused, await ErrorAsync(restarted));
        }

        using (var unknown = await processor.PostAsJsonAsync($"/api/payroll/jobs/{Guid.NewGuid()}/restart", new { }))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var run = await RunAsync(host, approved);
        Assert.Equal((PayrollRunStatus.Approved, true, true, 900m), (run.Status, run.ResultsFrozen, run.ValidatedAtCalculate, run.TotalNetPay));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_duplicate_bank_reference_at_the_posting_is_409_with_its_arabic_message_never_a_500()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var runId = await PlantAsync(host, PayrollRunStatus.Approved, 1);
        var start = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await host.WithDbAsync(async db =>
        {
            db.Add(new PayrollRunApproval { TenantId = Tenant, PayrollRunId = runId, ApproverId = Approver, Decision = ApprovalDecision.Approved, DecidedAt = DateTime.UtcNow });
            db.Add(new PayrollAuditRecord { TenantId = Tenant, PayrollRunId = runId, ClosedBy = Guid.NewGuid(), ClosedAt = DateTime.UtcNow, Status = AuditStatus.Closed, Findings = "لا ملاحظات" });
            // The batch the posting is about to write is there already, under the same reference.
            db.Add(new PaymentBatch
            {
                TenantId = Tenant,
                PayrollRunId = runId,
                TotalAmount = 900m,
                Status = PaymentBatchStatus.Pending,
                BankTransferReference = $"PAY-{start:yyyyMM}-{runId:N}"[..32],
            });
            return await db.SaveChangesAsync();
        });
        using var poster = As(host, Poster, PosterRoles);

        using var posted = await poster.PostAsync($"/api/payroll/runs/{runId}/post", null);

        Assert.Equal(HttpStatusCode.Conflict, posted.StatusCode);
        Assert.Equal(UniqueConstraintMessageRegistry.MessageForPaymentBatchReference, await ErrorAsync(posted));
        var run = await RunAsync(host, runId);
        Assert.Equal((PayrollRunStatus.Approved, (Guid?)null), (run.Status, run.PostedBy));
        Assert.Equal(1, await host.WithDbAsync(db => db.Set<PaymentBatch>().IgnoreQueryFilters().CountAsync(b => b.PayrollRunId == runId)));
    }
}
