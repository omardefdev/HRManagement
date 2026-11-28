using System.Diagnostics;
using System.Net;
using System.Text.Json;
using static HRManagement.AcceptanceTests.SeparationJourney;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-SEP-60i (PLAN-60, P-12) on the production-like profile, in the isolated group: it stops <c>hr-contracts-web</c> and empties its
/// outcome queue. Two outcomes are lost while HR.Contracts is down; the withdrawal of an ended instance applies its outcome
/// (<c>AlreadyTerminal</c>), and the sweep applies the other's after <c>StaleAfterMinutes</c>. A forged outcome naming an unlinked
/// instance is ignored and logged.
/// </summary>
[RequiredAcceptance]
public class AcSep60OutcomeSweepTests
{
    private const string Template = AcceptanceLeaverFixture.TemplatePersona;
    private const string Service = "hr-contracts-web";

    /// <summary>docker-compose.acceptance-prodlike.yml: WorkflowApproval__Sweep__StaleAfterMinutes and IntervalMinutes of hr-contracts-web.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private static readonly string ContractsDirect =
        (Environment.GetEnvironmentVariable("HR_CONTRACTS_DIRECT_URL") ?? "http://localhost:6220").TrimEnd('/') + "/";

    private static async Task WaitReadyAsync(string baseUrl)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddMinutes(5);
        do
        {
            try
            {
                using var response = await http.GetAsync(baseUrl + "health/ready");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Still starting.
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        } while (DateTime.UtcNow < deadline);

        Assert.Fail($"{Service} is not ready at {baseUrl}.");
    }

    /// <summary>HR.Contracts' queue of the outcome consumer (MassTransit's kebab-case endpoint), and its message count.</summary>
    private static async Task<(string Name, long Messages)> OutcomeQueueAsync()
    {
        using var http = AcInt37ProductionWiringTests.RabbitClient();
        using var doc = JsonDocument.Parse(await http.GetStringAsync("api/queues/%2F?columns=name,messages"));
        foreach (var queue in doc.RootElement.EnumerateArray())
        {
            var name = queue.GetProperty("name").GetString()!;
            if (name.Contains("separation-approval-outcome", StringComparison.Ordinal)
                && !name.EndsWith("_error", StringComparison.Ordinal) && !name.EndsWith("_skipped", StringComparison.Ordinal))
                return (name, queue.TryGetProperty("messages", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt64() : 0);
        }

        Assert.Fail("No separation-approval-outcome queue on the broker.");
        return default;
    }

    private static async Task<long> PollQueueAsync(Func<long, bool> until, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        var (_, messages) = await OutcomeQueueAsync();
        while (!until(messages) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            (_, messages) = await OutcomeQueueAsync();
        }

        return messages;
    }

    private static async Task PurgeAsync(string queue)
    {
        using var http = AcInt37ProductionWiringTests.RabbitClient();
        using var response = await http.DeleteAsync($"api/queues/%2F/{Uri.EscapeDataString(queue)}/contents");
        Assert.True(response.IsSuccessStatusCode, $"Purging {queue}: {(int)response.StatusCode}");
    }

    private static Task<DateTime> SubmittedAtAsync(Guid record) =>
        ReadAsync(Contracts, $"SELECT to_char(\"SubmittedAt\", 'YYYY-MM-DD\"T\"HH24:MI:SS.MS') FROM \"SeparationRecords\" WHERE \"Id\"='{record:D}';")
            .ContinueWith(t => DateTime.SpecifyKind(DateTime.Parse(t.Result, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc));

    [SkippableFact]
    public async Task Ac60i_Outcomes_lost_while_HR_Contracts_is_down_are_applied_by_the_withdrawal_and_by_the_sweep()
    {
        var s = await Sep60Scenario.StartAsync("60i",
            "AC-SEP-60i: hr-contracts-web stopped, two outcomes lost (the queue purged); the withdrawal of B applies its outcome, the sweep " +
            "applies A's; a forged outcome for C is ignored",
            Maker, CoreStep, Management, Checker);
        s.Steps($"""
            1. Fixture: three copies. a-contracts-maker creates and submits, in this order, A (the sweep's), C (the negative's), then B
               (the withdrawal's), just before the stop: the sweep takes only records older than StaleAfterMinutes ({StaleAfter.TotalMinutes:0}),
               so B is still pending when it is withdrawn.
            2. hr-contracts-web is stopped. central-a and a-management-approver approve A and B fully from their inboxes; the outcome queue
               (RabbitMQ's management API) holds both outcomes; it is purged: the outcomes are lost.
            3. hr-contracts-web is started and ready. At once, a-contracts-maker withdraws B from its page: HR.Core answers that the instance
               had ended (AlreadyTerminal), and the page says so («انتهى مسار الاعتماد قبل السحب … معتمد»): B is Approved, not a draft.
            4. Negative: an Approved outcome naming an instance C is not linked to is published on the bus → ignored and logged by
               hr-contracts-web with that id; C still PendingApproval. C is then withdrawn by the maker → Draft.
            5. A becomes Approved through the sweep (WorkflowApprovalSweepJob), within StaleAfterMinutes + IntervalMinutes
               ({Interval.TotalMinutes:0}) of its submission plus the start-up; the measured delay is recorded.
            6. Cleanup: A, B (Approved) and C (Draft) are cancelled with a reason by a-contracts-checker.
            """);
        var template = await AcceptanceLeaverFixture.TemplateFingerprintAsync();
        var copyA = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        var copyC = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        var copyB = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        Guid? a = null, b = null, c = null;
        List<string> leftovers;
        object checks;
        try
        {
            using var maker = await ScreenSession.OpenAsync(Maker, s.Recorder);
            var effective = TodayInDamascus().AddDays(7);
            a = await CreateAsync(maker, copyA, "Resignation", effective, "create-a");
            c = await CreateAsync(maker, copyC, "Resignation", effective, "create-c");
            b = await CreateAsync(maker, copyB, "Resignation", effective, "create-b");
            var instanceA = await SubmitAsync(maker, a.Value, "submit-a");
            var instanceC = await SubmitAsync(maker, c.Value, "submit-c");
            var instanceB = await SubmitAsync(maker, b.Value, "submit-b");
            var submittedA = await SubmittedAtAsync(a.Value);
            var submittedB = await SubmittedAtAsync(b.Value);
            var (queue, queuedBefore) = await OutcomeQueueAsync();
            s.Before(new { a, b, c, instanceA, instanceB, instanceC, status = "PendingApproval", queue, queuedBefore });

            // 2. The outcomes, lost while HR.Contracts is down.
            var down = Stopwatch.StartNew();
            await AcS2s42TrustBoundaryTests.StopAsync(Service);
            long queued;
            long purged;
            TimeSpan startUp;
            try
            {
                await ApproveStandardAsync(instanceA, s.Recorder);
                await ApproveStandardAsync(instanceB, s.Recorder);
                Assert.Equal("Completed", (await InstanceAsync(instanceA)).Status);
                Assert.Equal("Completed", (await InstanceAsync(instanceB)).Status);
                queued = await PollQueueAsync(m => m >= queuedBefore + 2, TimeSpan.FromSeconds(60));
                Assert.True(queued >= queuedBefore + 2, $"The outcome queue holds {queued} message(s), not the two outcomes.");
                await PurgeAsync(queue);
                purged = await PollQueueAsync(m => m == 0, TimeSpan.FromSeconds(30));
                Assert.Equal(0, purged);
            }
            finally
            {
                var restart = Stopwatch.StartNew();
                await AcS2s42TrustBoundaryTests.StartAsync(Service);
                await WaitReadyAsync(ContractsDirect);
                startUp = restart.Elapsed;
            }

            s.Recorder.RecordTiming("hrContractsDownMs", down.ElapsedMilliseconds);
            s.Recorder.RecordTiming("hrContractsStartUpMs", (long)startUp.TotalMilliseconds);

            // 3. At once: B's withdrawal meets an ended instance and applies its outcome.
            var bBefore = await StatusAsync(b.Value);
            var bAge = DateTime.UtcNow - submittedB;
            Assert.True(bBefore == "PendingApproval",
                $"B is {bBefore} {bAge.TotalSeconds:0}s after its submission: the withdrawal must meet it pending (StaleAfterMinutes {StaleAfter.TotalMinutes:0}).");
            var withdrawn = await maker.PostRenderedAsync("withdraw-b", Details(b.Value), "withdraw",
                fields: new Dictionary<string, string> { ["reason"] = "AC-SEP-60i" });
            Assert.Contains("انتهى مسار الاعتماد قبل السحب", withdrawn.Alerts, StringComparison.Ordinal);
            Assert.Contains("معتمد", withdrawn.Alerts, StringComparison.Ordinal);
            Assert.Equal("Approved", await StatusAsync(b.Value));

            // 4. A forged outcome for C, naming an instance it is not linked to: ignored and logged.
            var stranger = Guid.NewGuid();
            var since = DateTime.UtcNow.AddSeconds(-5).ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            await CrossServiceJourney.PublishOutcomeAsync(Contracts, "SeparationRecord", c.Value, stranger, "Approved");
            Assert.Equal("PendingApproval", await StatusAsync(c.Value));
            var container = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync(Service);
            var (_, logs) = await AcceptanceMessagingHelper.RunProcessAsync("docker", "logs", "--since", since, container);
            Assert.Contains(stranger.ToString("D"), logs, StringComparison.OrdinalIgnoreCase);
            await maker.PostRenderedAsync("withdraw-c", Details(c.Value), "withdraw",
                fields: new Dictionary<string, string> { ["reason"] = "AC-SEP-60i" });
            Assert.Equal("Draft", await StatusAsync(c.Value));
            var cInstance = await InstanceAsync(instanceC);
            Assert.False(cInstance.Status is "NotStarted" or "InProgress", $"C's instance is still {cInstance.Status}.");

            // 5. A, by the sweep.
            var deadline = submittedA + StaleAfter + Interval + startUp + TimeSpan.FromSeconds(30);
            var aStatus = await PollStatusAsync(a.Value, deadline - DateTime.UtcNow > TimeSpan.Zero ? deadline - DateTime.UtcNow : TimeSpan.Zero, "Approved");
            var sweptAfter = DateTime.UtcNow - submittedA;
            Assert.True(aStatus == "Approved",
                $"A is {aStatus} {sweptAfter.TotalSeconds:0}s after its submission (allowed {(deadline - submittedA).TotalSeconds:0}s).");
            s.Recorder.RecordTiming("sweptAfterTheSubmissionMs", (long)sweptAfter.TotalMilliseconds);

            checks = new
            {
                copies = new { a = copyA.Label, b = copyB.Label, c = copyC.Label },
                records = new { a, b, c },
                instances = new { a = instanceA, b = instanceB, c = instanceC },
                queue,
                queuedWhileDown = queued,
                afterThePurge = purged,
                startUpMs = (long)startUp.TotalMilliseconds,
                b = new { statusBeforeTheWithdrawal = bBefore, ageAtTheWithdrawalSeconds = (long)bAge.TotalSeconds, answer = withdrawn.Alerts, status = "Approved" },
                c = new { forgedInstance = stranger, statusAfterTheForgedOutcome = "PendingApproval", logged = true, withdrawn = "Draft", instance = cInstance.Status },
                a = new
                {
                    status = "Approved",
                    sweptAfterTheSubmissionSeconds = (long)sweptAfter.TotalSeconds,
                    allowedSeconds = (long)(deadline - submittedA).TotalSeconds,
                    staleAfterMinutes = StaleAfter.TotalMinutes,
                    intervalMinutes = Interval.TotalMinutes
                }
            };
        }
        finally
        {
            leftovers = await CancelLeftoversAsync(s.Recorder, a, b, c);
        }

        Assert.Empty(leftovers);
        Assert.Equal(template, await AcceptanceLeaverFixture.TemplateFingerprintAsync());
        await s.AuditAsync(a, b, c);
        s.Checks(checks);
        s.Recorder.WriteResult("Pass");
    }
}
