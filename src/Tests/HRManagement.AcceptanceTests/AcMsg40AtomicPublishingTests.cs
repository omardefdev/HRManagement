using System.Net;
using System.Text.RegularExpressions;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-40 (AC-MSG-40a..h) on the prod-like acceptance stack (<c>hrm-acceptance</c> with
/// docker-compose.acceptance-prodlike.yml): every publish writes its <c>OutboxMessage</c> row in the transaction that
/// commits the state, consumers publish through the consumer outbox, and the replay tool repairs what was lost before.
/// Setup rows are inserted with SQL where the UI path to them is not the subject (an approved promotion, an injected
/// "before the fix" row); the action under test always goes through the gateway. Outbox rows are deleted once delivered,
/// so "a row was written" is read from the <c>OutboxMessage</c> sequence (it never goes back).
/// </summary>
[RequiredAcceptance]
[Collection(AcMsg40Collection.Name)]
public class AcMsg40AtomicPublishingTests
{
    private const string Ac = "AC-MSG-40";
    private static readonly Guid TenantA = AcceptanceSeedIds.AgencyAId;
    private static readonly TimeSpan DeliveryWindow = TimeSpan.FromSeconds(60);

    // ------------------------------------------------------------------------------------------------ helpers

    private static EvidenceRecorder Recorder(string key, string scenario, string persona)
    {
        var recorder = new EvidenceRecorder(Ac, Guid.NewGuid().ToString("D")) { ScenarioKey = key };
        recorder.WriteMetadata(scenario, persona);
        return recorder;
    }

    private static Task<string> SqlTextAsync(string database, string sql) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(database, sql);

    private static Task<long> SqlScalarAsync(string database, string sql) =>
        AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(database, sql);

    /// <summary>The last <c>OutboxMessage</c> sequence value of a database (0 before the first row).</summary>
    internal static Task<long> OutboxSequenceAsync(string database) =>
        SqlScalarAsync(database,
            "SELECT CASE WHEN is_called THEN last_value ELSE 0 END FROM \"OutboxMessage_SequenceNumber_seq\";");

    internal static async Task<long> PollAsync(Func<Task<long>> read, Func<long, bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        long value;
        do
        {
            value = await read();
            if (done(value))
                return value;
            await Task.Delay(TimeSpan.FromSeconds(2));
        } while (DateTime.UtcNow < deadline);

        return value;
    }

    internal static string RequireAntiforgeryToken(string html)
    {
        var match = Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "No antiforgery token on the page.");
        return WebUtility.HtmlDecode(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
    }

    private static async Task<HttpResponseMessage> PostUiFormAsync(
        HttpClient http, string pagePath, string actionPath, IDictionary<string, string>? fields = null)
    {
        var page = await http.GetAsync(pagePath);
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {pagePath} -> {(int)page.StatusCode}");
        var form = new Dictionary<string, string>(fields ?? new Dictionary<string, string>())
        {
            ["__RequestVerificationToken"] = RequireAntiforgeryToken(html)
        };
        return await http.PostAsync(actionPath, new FormUrlEncodedContent(form));
    }

    private static async Task<Guid> OpenEmployeeAsync()
    {
        var text = await SqlTextAsync("hr_core_db",
            $"SELECT \"EmployeeId\" FROM \"EmploymentRecords\" WHERE \"EndDate\" IS NULL AND \"TenantId\" = '{TenantA:D}' " +
            "ORDER BY \"EmployeeId\" LIMIT 1;");
        Assert.True(Guid.TryParse(text, out var id), $"No employee with an open employment record in agency A: {text}");
        return id;
    }

    /// <summary>How often <paramref name="marker"/> occurs in the employee's open employment record notes.</summary>
    private static Task<long> NoteOccurrencesAsync(Guid employeeId, string marker) =>
        SqlScalarAsync("hr_core_db",
            "SELECT COALESCE(SUM((length(coalesce(\"Notes\",'')) - length(replace(coalesce(\"Notes\",''), " +
            $"'{marker}', ''))) / length('{marker}')), 0) FROM \"EmploymentRecords\" WHERE \"EmployeeId\" = '{employeeId:D}';");

    private static Task<long> ReceiptsAsync(string database, Guid messageId) =>
        SqlScalarAsync(database,
            $"SELECT COUNT(*) FROM \"IntegrationMessageReceipts\" WHERE \"MessageId\" = '{messageId:D}';");

    private static Task<long> ReceiptsForConsumerAsync(string database, string consumerSuffix) =>
        SqlScalarAsync(database,
            $"SELECT COUNT(*) FROM \"IntegrationMessageReceipts\" WHERE \"ConsumerType\" LIKE '%{consumerSuffix}';");

    /// <summary>Publishes <paramref name="values"/> again with <paramref name="messageId"/> (the AC-16 duplicate tool).</summary>
    private static async Task RedeliverAsync<T>(object values, Guid messageId) where T : class
    {
        var (bus, provider) = await AcceptanceMessagingHelper.ConnectBusAsync();
        try
        {
            var publish = provider.GetRequiredService<IPublishEndpoint>();
            await publish.Publish<T>(values, ctx =>
            {
                ctx.MessageId = messageId;
                ctx.Headers.Set(TenantIdMiddleware.HeaderName, TenantA.ToString("D"));
            });
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await bus.StopAsync();
            if (provider is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }
    }

    // ------------------------------------------------------------------------------------------------ 40a

    [SkippableFact]
    public async Task Ac40a_promotion_execution_writes_its_outbox_row_and_reaches_hr_core_once()
    {
        var r = Recorder("40a", "Promotion executed: IPromotionExecuted in the same save, delivered once to HR.Core", "a-movements");
        AcceptanceGate.RequireEnabled(r);
        var persona = AcceptanceHarness.RequirePersona("a-movements");

        var employeeId = await OpenEmployeeAsync();
        var promotionId = Guid.NewGuid();
        var decisionRef = $"MSG40A-{promotionId:N}"[..20];
        await SqlTextAsync("hr_movements_db", $"""
            INSERT INTO "PromotionRequests" ("Id","EmployeeId","SourceLegalRegimeId","FromGrade","ToGrade","FromStep","ToStep",
              "TransitionDecisionRef","EffectiveDate","Status","IsCancelled","TenantId","CreatedAt","UpdatedAt")
            VALUES ('{promotionId:D}','{employeeId:D}',NULL,5,4,3,1,'{decisionRef}',CURRENT_DATE - 1,1,false,'{TenantA:D}',now(),now());
            INSERT INTO "MovementChecklists" ("Id","MovementKind","CorrelationId","Step","IsCompleted","IsRequired","CompletedAt",
              "EvidenceRef","SlaHours","TenantId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(),3,'{promotionId:D}',0,true,true,now(),'msg40a:recorded',24,'{TenantA:D}',now(),now()),
                   (gen_random_uuid(),3,'{promotionId:D}',1,true,true,now(),'msg40a:approved',24,'{TenantA:D}',now(),now());
            SELECT 1;
            """);

        var messageId = DeterministicMessageId.For<IPromotionExecuted>(promotionId);
        var sequenceBefore = await OutboxSequenceAsync("hr_movements_db");
        var consumerReceiptsBefore = await ReceiptsForConsumerAsync("hr_core_db", "promotion-executed");
        r.WriteBefore(new { promotionId, employeeId, decisionRef, messageId, sequenceBefore, consumerReceiptsBefore });

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, Guid.NewGuid().ToString("D"), allowAutoRedirect: false);
        var execute = await PostUiFormAsync(http,
            $"ui/hr-movements/PromotionRequests/Details/{promotionId:D}",
            $"ui/hr-movements/PromotionRequests/Execute/{promotionId:D}");
        r.CaptureHttp("execute", "POST", $"/ui/hr-movements/PromotionRequests/Execute/{promotionId:D}", (int)execute.StatusCode,
            new { location = execute.Headers.Location?.ToString() });

        var status = await SqlScalarAsync("hr_movements_db", $"SELECT \"Status\" FROM \"PromotionRequests\" WHERE \"Id\" = '{promotionId:D}';");
        var sequenceAfter = await OutboxSequenceAsync("hr_movements_db");
        var receipts = await PollAsync(() => ReceiptsAsync("hr_core_db", messageId), n => n >= 1, DeliveryWindow);
        var consumerReceiptsAfter = await ReceiptsForConsumerAsync("hr_core_db", "promotion-executed");
        var notes = await NoteOccurrencesAsync(employeeId, decisionRef);
        var checks = new
        {
            promotionId, messageId, executeStatus = (int)execute.StatusCode, promotionStatus = status,
            outboxRowsWritten = sequenceAfter - sequenceBefore, receipts, consumerReceiptsBefore, consumerReceiptsAfter,
            employmentNotes = notes,
        };
        r.WriteScenarioDbChecks(checks);
        Assert.Equal(HttpStatusCode.Redirect, execute.StatusCode);
        Assert.Equal(2, status);
        Assert.True(sequenceAfter > sequenceBefore, $"No OutboxMessage row was written in hr_movements_db ({sequenceBefore} -> {sequenceAfter}).");
        Assert.Equal(1, receipts);
        Assert.Equal(1, notes);

        // Negative: the same message again (deterministic MessageId) is skipped by HR.Core's inbox.
        await RedeliverAsync<IPromotionExecuted>(new
        {
            PromotionRequestId = promotionId,
            EmployeeId = employeeId,
            SourceLegalRegimeId = (string?)null,
            FromGrade = 5, ToGrade = 4, FromStep = 3, ToStep = 1,
            TransitionDecisionRef = decisionRef,
            EffectiveDate = DateTime.UtcNow.Date.AddDays(-1),
            CorrelationId = Guid.NewGuid(),
            SourceSystem = "HR.Movements"
        }, messageId);
        await Task.Delay(TimeSpan.FromSeconds(8));
        var receiptsAfterDuplicate = await ReceiptsAsync("hr_core_db", messageId);
        var notesAfterDuplicate = await NoteOccurrencesAsync(employeeId, decisionRef);
        r.WriteAfter(new { receiptsAfterDuplicate, notesAfterDuplicate });
        Assert.Equal(1, receiptsAfterDuplicate);
        Assert.Equal(1, notesAfterDuplicate);

        r.WriteScenarioSteps(
            "1. SQL: an approved promotion (effective yesterday) with its RequestRecorded and Approved checklist steps, for an employee with an open employment record.\n" +
            "2. Gateway UI (a-movements): POST PromotionRequests/Execute -> 302; the request is Executed.\n" +
            "3. hr_movements_db: the OutboxMessage sequence advanced (a row was written with the state).\n" +
            "4. hr_core_db: one inbox receipt for the deterministic MessageId UUIDv5(IPromotionExecuted:<id>:v1) and one note in the employment record within 60 s.\n" +
            "5. Negative: the same message published again with the same MessageId -> still one receipt and one note.");
        r.WriteResult("Pass");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AcMsg40Collection
{
    public const string Name = "AC-MSG-40 prod-like stack";
}
