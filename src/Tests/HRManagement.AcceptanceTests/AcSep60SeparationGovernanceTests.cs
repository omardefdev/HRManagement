using System.Net;
using System.Text.RegularExpressions;
using static HRManagement.AcceptanceTests.SeparationJourney;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// AC-SEP-60a..h (PLAN-60, P-12) on the production-like profile: the separation journey unblocked and its decision governed. Each scenario
/// takes its own copies of the leaver template (<see cref="AcceptanceLeaverFixture"/>), relies on no other, ends every instance it starts,
/// and cancels every record it does not execute, so the class runs first, last or twice on one stack. One person per role; every
/// approval from HR.Core's inbox. AC-SEP-60i (the sweep and the withdrawal of an ended instance) stops <c>hr-contracts-web</c>, and is
/// <see cref="AcSep60OutcomeSweepTests"/> in the isolated group.
/// </summary>
[RequiredAcceptance]
public class AcSep60SeparationGovernanceTests
{
    private const string Template = AcceptanceLeaverFixture.TemplatePersona;
    private static readonly TimeSpan SagaWindow = TimeSpan.FromSeconds(90);

    private static string Rendered(bool rendered) => rendered ? "rendered" : "not rendered";

    /// <summary>Which layer refused an approval post (AC-SEP-60c records it; the instance's state is what the test asserts).</summary>
    private static string RefusalLayer(ScreenPage opened, ScreenPage attempt)
    {
        static bool Denied(HttpStatusCode status) => status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized;
        if (Denied(attempt.Status))
            return Denied(opened.Status)
                ? $"the module policy (the gateway's or HR.Core's): the approval page answers {(int)opened.Status}, the post {(int)attempt.Status}"
                : $"HR.Core's action policy: the page opens, the post answers {(int)attempt.Status}";
        if (attempt.Status == HttpStatusCode.BadRequest)
            return $"HR.Core's antiforgery check: the approval page answered {(int)opened.Status}, so the post carried no token";
        return $"the engine's role: the post reaches the engine, which refuses it ({(int)attempt.Status}: {attempt.Alerts})";
    }

    // ------------------------------------------------------------------------------------------------ a

    [SkippableFact]
    public async Task Ac60a_The_maker_submits_and_two_people_approve_from_HR_Cores_inbox()
    {
        var s = await Sep60Scenario.StartAsync("60a",
            "AC-SEP-60a: a Resignation submitted by the maker, approved by central-a (step 1) and the management approver (step 2)",
            Maker, CoreStep, Management);
        s.Steps("""
            1. Fixture: a new copy of the leaver template (HR.Core's Employees/Create, HR.Contracts' Contracts/Create).
            2. a-contracts-maker creates a Resignation on SeparationRecords/Create → Draft, no instance.
            3. He submits it from the record's page → PendingApproval, with an engine instance of SEPARATION-APPROVAL at step 1.
            4. central-a approves step 1 from his WorkInbox, a-management-approver step 2 from his → the record Approved within 30 seconds
               (HR.Core's outcome through the outbox, HR.Contracts' consumer), decided by the management approver.
            5. Cleanup: the record, not executed, is cancelled with a reason by a-contracts-checker.
            """);
        var template = await AcceptanceLeaverFixture.TemplateFingerprintAsync();
        var copy = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        Guid? record = null;
        var created = new List<Guid>();
        List<string> leftovers;
        object checks;
        try
        {
            using var maker = await ScreenSession.OpenAsync(Maker, s.Recorder);
            record = await CreateAsync(maker, copy, "Resignation", TodayInDamascus().AddDays(7), track: created);
            var draft = await StateAsync(record.Value);
            Assert.Equal(("Draft", (Guid?)null), draft);
            s.Before(new { copy.Label, record, draft.Status });

            var instance = await SubmitAsync(maker, record.Value);
            var pending = await InstanceAsync(instance);
            Assert.Equal((StandardCode, 1), (pending.Code, pending.Step));

            await ApproveStandardAsync(instance, s.Recorder);
            var delayMs = await AwaitStatusAsync(record.Value, "Approved");
            s.Recorder.RecordTiming("approvedAfterTheLastStepMs", delayMs);

            var actors = await ActorsAsync(record.Value);
            var makerId = (await PersonaIdAsync(Maker)).ToString("D");
            Assert.Equal(makerId, actors.SubmittedBy);
            Assert.Equal((await PersonaIdAsync(Management)).ToString("D"), actors.DecidedBy);
            var ended = await InstanceAsync(instance);
            Assert.Equal("Completed", ended.Status);
            checks = new
            {
                copy = copy.Label,
                record,
                instance,
                workflowCode = pending.Code,
                status = "Approved",
                approvedWithinMs = delayMs,
                submittedBy = actors.SubmittedBy,
                decidedBy = actors.DecidedBy,
                instanceStatus = ended.Status
            };
        }
        finally
        {
            leftovers = await CancelLeftoversAsync(s.Recorder, created);
        }

        Assert.Empty(leftovers);
        Assert.Equal(template, await AcceptanceLeaverFixture.TemplateFingerprintAsync());
        await s.AuditAsync(record);
        s.Checks(checks);
        s.Recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ b

    [SkippableFact]
    public async Task Ac60b_The_checker_issues_the_decision_records_the_notification_and_executes_on_the_effective_date()
    {
        var s = await Sep60Scenario.StartAsync("60b",
            "AC-SEP-60b: decision DEC-SEP-YYYY-#####, EmployeeNotified with evidence, the execution on the effective date in Damascus; " +
            "negatives: before the decision, before the date",
            Checker, Maker, CoreStep, Management);
        s.Steps("""
            1. Fixture: two copies. Copy 1 effective today in Damascus, copy 2 effective tomorrow; each created and submitted by
               a-contracts-maker and approved from the inboxes (central-a, a-management-approver) → Approved.
            2. a-contracts-checker issues copy 1's decision on its page → DEC-SEP-<year>-#####, the item DecisionIssued completed by the
               service. He completes EmployeeNotified on the page with an evidence reference. The page renders the execution; he executes →
               Executing at once (or later in the saga), executed by him, the contract Terminated with its end date the effective date,
               one saga. The saga's end on this stack is recorded (Completed, or AwaitingIntervention with its party): PLAN-62's truth.
            3. Negative, copy 2: the execution before the decision → refused («دون قرار إداري»), still Approved, no saga; after the decision
               and the notification → refused naming the effective date («لم يحن بعد بتوقيت دمشق»), still Approved, the contract
               untouched, no saga.
            4. Cleanup: copy 2's record (Approved, its decision kept) is cancelled with a reason; copy 1's executed record keeps its saga.
            """);
        var template = await AcceptanceLeaverFixture.TemplateFingerprintAsync();
        var today = TodayInDamascus();
        var due = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        var early = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        Guid? executed = null;
        Guid? waiting = null;
        var created = new List<Guid>();
        List<string> leftovers;
        object checks;
        try
        {
            using var maker = await ScreenSession.OpenAsync(Maker, s.Recorder);
            using var checker = await ScreenSession.OpenAsync(Checker, s.Recorder);
            (executed, _) = await ApprovedAsync(maker, due, today, s.Recorder, created);
            (waiting, _) = await ApprovedAsync(maker, early, today.AddDays(1), s.Recorder, created);
            s.Before(new { due = due.Label, early = early.Label, executed, waiting, status = "Approved", today = Iso(today) });

            // Copy 1: the decision, the notification, the execution.
            var number = await IssueDecisionAsync(checker, executed.Value);
            Assert.True(IsDecisionNumber(number, today), number);
            Assert.StartsWith("true|", await ItemAsync(executed.Value, "DecisionIssued"));
            var evidence = $"AC-SEP-60-NOTICE-{due.Label}";
            await CompleteItemAsync(checker, executed.Value, "EmployeeNotified", evidence);
            var notified = await ItemAsync(executed.Value, "EmployeeNotified");
            Assert.StartsWith("true|", notified);
            Assert.EndsWith($"|{evidence}", notified);
            Assert.True(await checker.RendersAsync(Details(executed.Value), "execute"), "The record's page offers no execution to the checker.");

            var execution = await ExecuteAsync(checker, executed.Value);
            var atOnce = await StatusAsync(executed.Value);
            Assert.True(atOnce is "Executing" or "AwaitingIntervention" or "Completed", $"After the execution: {atOnce} {execution.Alerts}");
            Assert.Equal((await PersonaIdAsync(Checker)).ToString("D"), (await ActorsAsync(executed.Value)).ExecutedBy);
            Assert.Equal($"Terminated|{Iso(today)}", await ContractAsync(due.ContractId));
            Assert.Equal(1, await SagaStartedAsync(executed.Value));
            var sagaEnd = await SagaEndAsync(executed.Value, SagaWindow);

            // Copy 2: before the decision, then before the date.
            var offeredEarly = await checker.RendersAsync(Details(waiting.Value), "execute");
            var noDecision = await ExecuteAsync(checker, waiting.Value, "execute-before-decision");
            Assert.Contains("دون قرار إداري", noDecision.Alerts, StringComparison.Ordinal);
            Assert.Equal("Approved", await StatusAsync(waiting.Value));
            var earlyNumber = await IssueDecisionAsync(checker, waiting.Value);
            await CompleteItemAsync(checker, waiting.Value, "EmployeeNotified", $"AC-SEP-60-NOTICE-{early.Label}");
            var notYet = await ExecuteAsync(checker, waiting.Value, "execute-before-date");
            Assert.Contains("لم يحن بعد بتوقيت دمشق", notYet.Alerts, StringComparison.Ordinal);
            Assert.Contains(Iso(today.AddDays(1)), notYet.Alerts, StringComparison.Ordinal);
            Assert.Equal("Approved", await StatusAsync(waiting.Value));
            Assert.Equal("Pending|", await ContractAsync(early.ContractId));
            Assert.Equal(0, await SagasOfAsync(waiting.Value));

            checks = new
            {
                executed = new
                {
                    copy = due.Label,
                    record = executed,
                    decision = number,
                    decisionIssuedItem = "completed by the service",
                    employeeNotified = notified,
                    statusAtOnce = atOnce,
                    contract = $"Terminated|{Iso(today)}",
                    sagas = 1,
                    sagaEnd
                },
                negatives = new
                {
                    copy = early.Label,
                    record = waiting,
                    executeRenderedBeforeTheDecision = Rendered(offeredEarly),
                    beforeTheDecision = noDecision.Alerts,
                    decision = earlyNumber,
                    beforeTheDate = notYet.Alerts,
                    status = "Approved",
                    contract = "Pending",
                    sagas = 0
                }
            };
        }
        finally
        {
            leftovers = await CancelLeftoversAsync(s.Recorder, created);
        }

        Assert.Empty(leftovers);
        Assert.Equal(template, await AcceptanceLeaverFixture.TemplateFingerprintAsync());
        await s.AuditAsync(executed, waiting, due.ContractId, early.ContractId);
        s.Checks(checks);
        s.Recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ c

    [SkippableFact]
    public async Task Ac60c_The_creator_neither_approves_his_record_nor_issues_its_decision()
    {
        var s = await Sep60Scenario.StartAsync("60c",
            "AC-SEP-60c: the maker's approval of step 1 of his own record is refused (the layer recorded); after the approvals, his decision is refused",
            Maker, CoreStep, Management, Checker);
        s.Steps("""
            1. Fixture: a copy. a-contracts-maker creates and submits a Resignation → PendingApproval, step 1.
            2. Negative: a-contracts-maker opens step 1's approval page and posts its approval in HR.Core → refused; the instance is still
               at step 1, in progress, the record PendingApproval. He holds neither the step's role (hr-core) nor HR.Core's module, so the
               refusal is a role's before any separation-of-duties rule (P-12): the layer that answered is recorded. The exclusion of a
               creator who holds the role is Sep060ExclusionTests' (P-7).
            3. central-a and a-management-approver approve from their inboxes → Approved.
            4. Negative: a-contracts-maker posts the record's decision → refused by the issuer rule («يصدره صاحب صلاحية آخر»); no decision.
            5. Cleanup: the record is cancelled with a reason by a-contracts-checker.
            """);
        var template = await AcceptanceLeaverFixture.TemplateFingerprintAsync();
        var copy = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        Guid? record = null;
        var created = new List<Guid>();
        List<string> leftovers;
        object checks;
        try
        {
            using var maker = await ScreenSession.OpenAsync(Maker, s.Recorder);
            record = await CreateAsync(maker, copy, "Resignation", TodayInDamascus().AddDays(7), track: created);
            var instance = await SubmitAsync(maker, record.Value);
            s.Before(new { copy.Label, record, instance, status = "PendingApproval", step = 1 });

            var page = CustodyJourney.ApprovalPage(instance);
            var opened = await maker.GetAsync("c-maker-opens-step-1", page);
            var attempt = await maker.PostAsync("c-maker-approves-step-1", page, $"ui/hr-core/WorkflowApprovals/ApproveWorkflow/{instance:D}",
                new Dictionary<string, string> { ["Comments"] = "AC-SEP-60c" }, requireToken: false);
            var layer = RefusalLayer(opened, attempt);
            var afterAttempt = await InstanceAsync(instance);
            Assert.Equal(("InProgress", 1), (afterAttempt.Status, afterAttempt.Step));
            Assert.Equal("PendingApproval", await StatusAsync(record.Value));

            await ApproveStandardAsync(instance, s.Recorder);
            await AwaitStatusAsync(record.Value, "Approved");
            var issueOffered = await maker.RendersAsync(Details(record.Value), "issue-decision");
            var issued = await maker.PostAsync("c-maker-issues", Details(record.Value), ActionPath("IssueDecision", record.Value));
            Assert.Contains("يصدره صاحب صلاحية آخر", issued.Alerts, StringComparison.Ordinal);
            Assert.Equal("", await DecisionNumberAsync(record.Value));

            checks = new
            {
                copy = copy.Label,
                record,
                instance,
                makerApprovalPage = (int)opened.Status,
                makerApprovalPost = (int)attempt.Status,
                layer,
                instanceAfterTheAttempt = new { afterAttempt.Status, afterAttempt.Step },
                approved = true,
                issueDecisionRenderedForTheCreator = Rendered(issueOffered),
                makerDecision = issued.Alerts,
                decision = "none"
            };
        }
        finally
        {
            leftovers = await CancelLeftoversAsync(s.Recorder, created);
        }

        Assert.Empty(leftovers);
        Assert.Equal(template, await AcceptanceLeaverFixture.TemplateFingerprintAsync());
        await s.AuditAsync(record);
        s.Checks(checks);
        s.Recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ d

    [SkippableFact]
    public async Task Ac60d_Through_the_gateway_the_management_approver_cannot_execute()
    {
        var s = await Sep60Scenario.StartAsync("60d",
            "AC-SEP-60d: through the gateway, a-management-approver's Step3Execute (and Execute) on a record ready to execute → 403",
            Management, Maker, CoreStep, Checker);
        s.Steps("""
            1. Fixture: a copy, effective today. a-contracts-maker creates and submits; central-a and a-management-approver approve →
               Approved; a-contracts-checker issues the decision and completes EmployeeNotified: only the policy stands before the execution.
            2. Negative: a-management-approver posts SeparationWizard/Step3Execute, then SeparationRecords/Execute, through the gateway →
               403 each; the record still Approved, the contract untouched, no saga. Without the gateway, Sep060ServicePolicyHttpTests
               gives the same role 403 from the action's policy (HrContracts.Approve).
            3. Cleanup: the record is cancelled with a reason by a-contracts-checker.
            """);
        var template = await AcceptanceLeaverFixture.TemplateFingerprintAsync();
        var copy = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        Guid? record = null;
        var created = new List<Guid>();
        List<string> leftovers;
        object checks;
        try
        {
            using var maker = await ScreenSession.OpenAsync(Maker, s.Recorder);
            using var checker = await ScreenSession.OpenAsync(Checker, s.Recorder);
            (record, _) = await ApprovedAsync(maker, copy, TodayInDamascus(), s.Recorder, created);
            await IssueDecisionAsync(checker, record.Value);
            await CompleteItemAsync(checker, record.Value, "EmployeeNotified", $"AC-SEP-60-NOTICE-{copy.Label}");
            s.Before(new { copy.Label, record, status = "Approved", decision = "issued", employeeNotified = "completed" });

            using var management = await ScreenSession.OpenAsync(Management, s.Recorder);
            var wizard = await management.PostAsync("d-step3-execute", Details(record.Value),
                $"ui/hr-contracts/SeparationWizard/Step3Execute/{record:D}", requireToken: false);
            var execute = await management.PostAsync("d-execute", Details(record.Value), ActionPath("Execute", record.Value), requireToken: false);
            Assert.Equal(HttpStatusCode.Forbidden, wizard.Status);
            Assert.Equal(HttpStatusCode.Forbidden, execute.Status);
            Assert.Equal("Approved", await StatusAsync(record.Value));
            Assert.Equal("Pending|", await ContractAsync(copy.ContractId));
            Assert.Equal(0, await SagasOfAsync(record.Value));

            checks = new
            {
                copy = copy.Label,
                record,
                step3Execute = (int)wizard.Status,
                execute = (int)execute.Status,
                status = "Approved",
                contract = "Pending",
                sagas = 0,
                withoutTheGateway = "Sep060ServicePolicyHttpTests (Testcontainers)"
            };
        }
        finally
        {
            leftovers = await CancelLeftoversAsync(s.Recorder, created);
        }

        Assert.Empty(leftovers);
        Assert.Equal(template, await AcceptanceLeaverFixture.TemplateFingerprintAsync());
        await s.AuditAsync(record);
        s.Checks(checks);
        s.Recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ e

    [SkippableFact]
    public async Task Ac60e_One_open_record_the_employees_own_contract_and_no_change_of_subject_after_the_submission()
    {
        var s = await Sep60Scenario.StartAsync("60e",
            "AC-SEP-60e: a second open record refused; another copy's contract refused; EmployeeId not editable after the submission",
            Maker, Checker);
        s.Steps("""
            1. Fixture: two copies. a-contracts-maker creates a Resignation for copy 1.
            2. Negative: a second record for copy 1 → refused («يوجد سجل فصل مفتوح لهذا الموظف»), still one open record.
            3. Negative: a record for copy 2 naming copy 1's contract → refused («العقد المختار ليس…»), copy 2 has no record.
            4. He submits copy 1's record. Negative: its Edit with copy 2's employee and contract → refused («وهو مسودة فقط»): the
               employee, the contract and the status unchanged.
            5. Cleanup: the record, pending, is cancelled with a reason by a-contracts-checker (the cancellation withdraws its instance).
            """);
        var template = await AcceptanceLeaverFixture.TemplateFingerprintAsync();
        var first = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        var second = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        Guid? record = null;
        var created = new List<Guid>();
        List<string> leftovers;
        object checks;
        try
        {
            using var maker = await ScreenSession.OpenAsync(Maker, s.Recorder);
            var effective = TodayInDamascus().AddDays(7);
            record = await CreateAsync(maker, first, "Resignation", effective, track: created);
            s.Before(new { first = first.Label, second = second.Label, record, openRecordsOfTheFirst = 1 });

            const string create = "ui/hr-contracts/SeparationRecords/Create";
            var duplicate = await maker.PostAsync("e-second-open-record", create, create,
                RecordFields(first.EmployeeId, first.ContractId, "Resignation", effective, "AC-SEP-60e"));
            Assert.True(duplicate.Says("يوجد سجل فصل مفتوح لهذا الموظف"), $"{(int)duplicate.Status} {ValidationErrors(duplicate.Html)}");
            Assert.Equal(1, await RecordsOfAsync(first.EmployeeId));

            var foreign = await maker.PostAsync("e-another-copys-contract", create, create,
                RecordFields(second.EmployeeId, first.ContractId, "Resignation", effective, "AC-SEP-60e"));
            Assert.True(foreign.Says("العقد المختار ليس"), $"{(int)foreign.Status} {ValidationErrors(foreign.Html)}");
            Assert.Equal(0, await RecordsOfAsync(second.EmployeeId));

            var instance = await SubmitAsync(maker, record.Value);
            var edit = $"ui/hr-contracts/SeparationRecords/Edit/{record:D}";
            var fields = RecordFields(second.EmployeeId, second.ContractId, "Resignation", effective, "AC-SEP-60e");
            fields["Id"] = record.Value.ToString("D");
            var edited = await maker.PostAsync("e-edit-after-submission", Details(record.Value), edit, fields);
            Assert.True(edited.Says("وهو مسودة فقط"), $"{(int)edited.Status} {ValidationErrors(edited.Html)} {edited.Alerts}");
            var subject = await ReadAsync(Contracts,
                $"SELECT \"EmployeeId\"::text || '|' || COALESCE(\"ContractId\"::text,'') || '|' || \"Status\" FROM \"SeparationRecords\" WHERE \"Id\"='{record:D}';");
            Assert.Equal($"{first.EmployeeId:D}|{first.ContractId:D}|PendingApproval", subject);
            Assert.Equal(0, await RecordsOfAsync(second.EmployeeId));

            checks = new
            {
                first = first.Label,
                second = second.Label,
                record,
                instance,
                secondOpenRecord = ValidationErrors(duplicate.Html),
                openRecordsOfTheFirst = 1,
                anotherCopysContract = ValidationErrors(foreign.Html),
                recordsOfTheSecond = 0,
                editAfterTheSubmission = ValidationErrors(edited.Html),
                subjectAfterTheEdit = subject
            };
        }
        finally
        {
            leftovers = await CancelLeftoversAsync(s.Recorder, created);
        }

        Assert.Empty(leftovers);
        Assert.Equal(template, await AcceptanceLeaverFixture.TemplateFingerprintAsync());
        await s.AuditAsync(record);
        s.Checks(checks);
        s.Recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ f

    [SkippableFact]
    public async Task Ac60f_An_exception_on_the_notification_takes_a_second_person_then_the_execution_passes()
    {
        var s = await Sep60Scenario.StartAsync("60f",
            "AC-SEP-60f: the maker asks an exception on EmployeeNotified, his own approval is refused, the checker's excepts it, the execution passes",
            Maker, Checker, CoreStep, Management);
        s.Steps("""
            1. Fixture: a copy, effective today. a-contracts-maker creates and submits; central-a and a-management-approver approve →
               Approved; a-contracts-checker issues the decision.
            2. a-contracts-maker requests an exception on EmployeeNotified from the record's page, with a reason.
            3. Negative: his own approval of it → refused («لا الطالب ولا منشئ سجل الفصل»), the item not excepted.
            4. a-contracts-checker approves it on the page → the item excepted, approved by him. He executes → Executing, the contract
               Terminated at the effective date, one saga; the saga's end recorded.
            """);
        var template = await AcceptanceLeaverFixture.TemplateFingerprintAsync();
        var today = TodayInDamascus();
        var copy = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        Guid? record = null;
        var created = new List<Guid>();
        List<string> leftovers;
        object checks;
        try
        {
            using var maker = await ScreenSession.OpenAsync(Maker, s.Recorder);
            using var checker = await ScreenSession.OpenAsync(Checker, s.Recorder);
            (record, _) = await ApprovedAsync(maker, copy, today, s.Recorder, created);
            var number = await IssueDecisionAsync(checker, record.Value);
            s.Before(new { copy.Label, record, status = "Approved", decision = number });

            const string reason = "AC-SEP-60f: الموظف خارج البلاد ويتعذر تبليغه قبل تاريخ النفاذ";
            await maker.PostRenderedAsync("f-request-exception", Details(record.Value), "request-exception", "EmployeeNotified",
                new Dictionary<string, string> { ["reason"] = reason });
            var requested = await ItemAsync(record.Value, "EmployeeNotified");
            var makerId = (await PersonaIdAsync(Maker)).ToString("D");
            Assert.StartsWith($"false|false|{makerId}|", requested);

            var own = await maker.PostAsync("f-maker-approves-own", Details(record.Value), ActionPath("ApproveStepException", record.Value),
                new Dictionary<string, string> { ["step"] = "EmployeeNotified" });
            Assert.Contains("لا الطالب ولا منشئ سجل الفصل", own.Alerts, StringComparison.Ordinal);
            Assert.StartsWith("false|false|", await ItemAsync(record.Value, "EmployeeNotified"));

            await checker.PostRenderedAsync("f-checker-approves", Details(record.Value), "approve-exception", "EmployeeNotified");
            var excepted = await ItemAsync(record.Value, "EmployeeNotified");
            var checkerId = (await PersonaIdAsync(Checker)).ToString("D");
            Assert.StartsWith($"false|true|{makerId}|{checkerId}|", excepted);

            var execution = await ExecuteAsync(checker, record.Value);
            var atOnce = await StatusAsync(record.Value);
            Assert.True(atOnce is "Executing" or "AwaitingIntervention" or "Completed", $"After the execution: {atOnce} {execution.Alerts}");
            Assert.Equal($"Terminated|{Iso(today)}", await ContractAsync(copy.ContractId));
            Assert.Equal(1, await SagaStartedAsync(record.Value));
            var sagaEnd = await SagaEndAsync(record.Value, SagaWindow);

            checks = new
            {
                copy = copy.Label,
                record,
                decision = number,
                requested,
                makersOwnApproval = own.Alerts,
                excepted,
                statusAtOnce = atOnce,
                contract = $"Terminated|{Iso(today)}",
                sagas = 1,
                sagaEnd
            };
        }
        finally
        {
            leftovers = await CancelLeftoversAsync(s.Recorder, created);
        }

        Assert.Empty(leftovers);
        Assert.Equal(template, await AcceptanceLeaverFixture.TemplateFingerprintAsync());
        await s.AuditAsync(record, copy.ContractId);
        s.Checks(checks);
        s.Recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ g

    private static async Task<string> PayrollFootprintAsync(Guid employee)
    {
        var id = employee.ToString("D");
        return await ReadAsync(Payroll,
            $"SELECT (SELECT count(*) FROM \"EmployeePayProfiles\" WHERE \"EmployeeId\"='{id}')::text || '|' || " +
            $"(SELECT count(*) FROM \"EndOfServiceSettlements\" WHERE \"EmployeeId\"='{id}')::text || '|' || " +
            $"(SELECT count(*) FROM \"PayrollAdjustments\" WHERE \"EmployeeId\"='{id}')::text || '|' || " +
            $"(SELECT count(*) FROM \"PaymentHolds\" WHERE \"EmployeeId\"='{id}')::text;");
    }

    /// <summary>What Payroll wrote for agency A since <paramref name="since"/>: settlements|adjustments|holds (no other class runs beside).</summary>
    private static Task<string> AgencyPayrollWritesAsync(DateTime since)
    {
        var tenant = HRManagement.ServiceDefaults.AcceptanceSeedIds.AgencyAId.ToString("D");
        var at = since.ToString("yyyy-MM-dd HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture);
        string Count(string table) => $"(SELECT count(*) FROM \"{table}\" WHERE \"TenantId\"='{tenant}' AND \"CreatedAt\" >= '{at}')::text";
        return ReadAsync(Payroll, $"SELECT {Count("EndOfServiceSettlements")} || '|' || {Count("PayrollAdjustments")} || '|' || {Count("PaymentHolds")};");
    }

    [SkippableFact]
    public async Task Ac60g_A_rejection_with_its_reason_leaves_no_saga_and_payroll_unchanged()
    {
        var s = await Sep60Scenario.StartAsync("60g",
            "AC-SEP-60g: a-management-approver rejects step 2 with a reason → Rejected with the reason, no saga, Payroll unchanged",
            Maker, CoreStep, Management, Checker);
        s.Steps("""
            1. Fixture: a copy. a-contracts-maker creates and submits a Resignation; central-a approves step 1 from his inbox.
            2. Negative: a-management-approver rejects step 2 from his inbox with a reason → the record Rejected with that reason, within
               30 seconds; no saga row; Payroll's rows for the copy (pay profiles, settlements, adjustments, holds) as they were (the copy's
               contract is pending: it has no profile, P-12's deviation), nothing written by Payroll for agency A since the submission
               (settlements, adjustments, holds), and the contract untouched.
            3. Cleanup: the rejected record is cancelled with a reason by a-contracts-checker.
            """);
        var template = await AcceptanceLeaverFixture.TemplateFingerprintAsync();
        var copy = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        Guid? record = null;
        var created = new List<Guid>();
        List<string> leftovers;
        object checks;
        try
        {
            using var maker = await ScreenSession.OpenAsync(Maker, s.Recorder);
            record = await CreateAsync(maker, copy, "Resignation", TodayInDamascus().AddDays(7), track: created);
            var payrollBefore = await PayrollFootprintAsync(copy.EmployeeId);
            var since = DateTime.UtcNow;
            s.Before(new { copy.Label, record, payroll = payrollBefore });

            var instance = await SubmitAsync(maker, record.Value);
            await CustodyJourney.ApproveFromInboxAsync(CoreStep, instance, s.Recorder);
            const string reason = "AC-SEP-60g: المستندات المرفقة لا تثبت تاريخ تقديم الاستقالة";
            await CustodyJourney.RejectFromInboxAsync(Management, instance, reason, s.Recorder);
            var delayMs = await AwaitStatusAsync(record.Value, "Rejected");
            var actors = await ActorsAsync(record.Value);
            Assert.Contains(reason, actors.RejectionReason, StringComparison.Ordinal);
            Assert.Equal(0, await SagasOfAsync(record.Value));
            var payrollAfter = await PayrollFootprintAsync(copy.EmployeeId);
            Assert.Equal(payrollBefore, payrollAfter);
            // And nothing for anyone in the agency: no settlement, adjustment or hold written since the submission.
            var agencyWrites = await AgencyPayrollWritesAsync(since);
            Assert.Equal("0|0|0", agencyWrites);
            Assert.Equal("Pending|", await ContractAsync(copy.ContractId));

            checks = new
            {
                copy = copy.Label,
                record,
                instance,
                status = "Rejected",
                rejectedWithinMs = delayMs,
                rejectionReason = actors.RejectionReason,
                sagas = 0,
                payroll = new
                {
                    before = payrollBefore,
                    after = payrollAfter,
                    columns = "profiles|settlements|adjustments|holds",
                    agencyWritesSinceTheSubmission = agencyWrites,
                    note = "the copy's contract is pending (P-12's recorded deviation): it has no profile to stay active"
                },
                contract = "Pending"
            };
        }
        finally
        {
            leftovers = await CancelLeftoversAsync(s.Recorder, created);
        }

        Assert.Empty(leftovers);
        Assert.Equal(template, await AcceptanceLeaverFixture.TemplateFingerprintAsync());
        await s.AuditAsync(record);
        s.Checks(checks);
        s.Recorder.WriteResult("Pass");
    }

    // ------------------------------------------------------------------------------------------------ h

    [SkippableFact]
    public async Task Ac60h_A_disciplinary_record_takes_three_steps_with_compliance_in_the_middle()
    {
        var s = await Sep60Scenario.StartAsync("60h",
            "AC-SEP-60h: a Disciplinary record → SEPARATION-APPROVAL-SENSITIVE, three steps; central-a refused the compliance step by its role",
            Maker, CoreStep, ComplianceStep, Management, Checker);
        s.Steps("""
            1. Fixture: a copy. a-contracts-maker creates and submits a Disciplinary record → PendingApproval on
               SEPARATION-APPROVAL-SENSITIVE (hr-core, compliance, hr-management-approver).
            2. central-a approves step 1 from his inbox → step 2.
            3. Negative: central-a posts the approval of step 2 (compliance) → refused by its role; the instance still at step 2.
            4. a-compliance approves step 2 and a-management-approver step 3, each from the inbox → Approved.
            5. Cleanup: the record is cancelled with a reason by a-contracts-checker.
            """);
        var template = await AcceptanceLeaverFixture.TemplateFingerprintAsync();
        var copy = await AcceptanceLeaverFixture.CreateAsync(Template, s.Recorder);
        Guid? record = null;
        var created = new List<Guid>();
        List<string> leftovers;
        object checks;
        try
        {
            using var maker = await ScreenSession.OpenAsync(Maker, s.Recorder);
            record = await CreateAsync(maker, copy, "Disciplinary", TodayInDamascus().AddDays(7), track: created);
            var instance = await SubmitAsync(maker, record.Value);
            var pending = await InstanceAsync(instance);
            Assert.Equal((SensitiveCode, 1), (pending.Code, pending.Step));
            s.Before(new { copy.Label, record, instance, workflowCode = pending.Code, step = 1 });

            await CustodyJourney.ApproveFromInboxAsync(CoreStep, instance, s.Recorder);
            Assert.Equal(2, (await InstanceAsync(instance)).Step);

            using var central = await ScreenSession.OpenAsync(CoreStep, s.Recorder);
            var page = CustodyJourney.ApprovalPage(instance);
            var opened = await central.GetAsync("h-central-opens-step-2", page);
            var offered = Regex.IsMatch(opened.Html, @"action=""/[^""]*/ApproveWorkflow/");
            var attempt = await central.PostAsync("h-central-approves-step-2", page, $"ui/hr-core/WorkflowApprovals/ApproveWorkflow/{instance:D}",
                new Dictionary<string, string> { ["Comments"] = "AC-SEP-60h" }, requireToken: false);
            var afterAttempt = await InstanceAsync(instance);
            Assert.Equal(("InProgress", 2), (afterAttempt.Status, afterAttempt.Step));

            await CustodyJourney.ApproveFromInboxAsync(ComplianceStep, instance, s.Recorder);
            Assert.Equal(3, (await InstanceAsync(instance)).Step);
            await CustodyJourney.ApproveFromInboxAsync(Management, instance, s.Recorder);
            var delayMs = await AwaitStatusAsync(record.Value, "Approved");

            checks = new
            {
                copy = copy.Label,
                record,
                instance,
                workflowCode = pending.Code,
                centralOnTheComplianceStep = new
                {
                    page = (int)opened.Status,
                    approveFormRendered = offered,
                    post = (int)attempt.Status,
                    alerts = attempt.Alerts,
                    instanceAfter = new { afterAttempt.Status, afterAttempt.Step }
                },
                status = "Approved",
                approvedWithinMs = delayMs
            };
        }
        finally
        {
            leftovers = await CancelLeftoversAsync(s.Recorder, created);
        }

        Assert.Empty(leftovers);
        Assert.Equal(template, await AcceptanceLeaverFixture.TemplateFingerprintAsync());
        await s.AuditAsync(record);
        s.Checks(checks);
        s.Recorder.WriteResult("Pass");
    }
}
