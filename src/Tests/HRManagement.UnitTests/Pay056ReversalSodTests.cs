using System.Text.Json;
using Dev.Analytics.Infrastructure.Consumers;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Features.PayrollRuns.Reversal;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Repositories;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-56 (G-PAY-06): the separation of duties of a reversal lives in the handler. The decider of a reversal request is neither its
/// requester nor the person who posted the run — whatever policies he holds, for an approval and for a rejection alike. The
/// requester may be the poster: he asks, and another person decides. No persona of the catalogue holds both the reversal and the
/// approval before PLAN-58, so the rule is proved here, on the handler.
/// </summary>
public sealed class Pay056ReversalSodTests : IAsyncLifetime
{
    private static readonly Guid Poster = Guid.Parse("56000000-0000-4000-8000-0000000000d1");
    private static readonly Guid Reverser = Guid.Parse("56000000-0000-4000-8000-0000000000d2");
    private static readonly Guid Approver = Guid.Parse("56000000-0000-4000-8000-0000000000d3");
    private static readonly DateTime PostedAt = new(2028, 2, 2, 8, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    private sealed class Publisher : IPayrollEventPublisher
    {
        public List<(Guid RunId, decimal Amount, Guid RequestId)> Reversed { get; } = [];

        public Task PublishPayrollProcessedAsync(Guid payrollRunId, DateTime periodStart, DateTime periodEnd, decimal totalNetPay, CancellationToken ct) =>
            Task.CompletedTask;

        public Task PublishPayrollReversedAsync(
            Guid payrollRunId, DateTime periodStart, DateTime periodEnd, decimal reversedNetPay, Guid reversalRequestId, DateTime reversedAt,
            CancellationToken ct)
        {
            Reversed.Add((payrollRunId, reversedNetPay, reversalRequestId));
            return Task.CompletedTask;
        }
    }

    private readonly Publisher _events = new();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private PayrollDbContext Db() => new(new DbContextOptionsBuilder<PayrollDbContext>().UseSqlite(_connection).Options);

    /// <summary>A run posted by <see cref="Poster"/> through the run's own methods, with its pending batch.</summary>
    private async Task<Guid> PostedRunAsync()
    {
        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2028, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            RunDate = new DateTime(2028, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = PayrollRunStatus.Approved,
            TotalNetPay = 900m,
            ValidatedAtCalculate = true,
            ResultsFrozen = true,
        };
        run.Post(Poster, PostedAt);
        await using var db = Db();
        db.PayrollRuns.Add(run);
        db.PaymentBatches.Add(new PaymentBatch { PayrollRunId = run.Id, TotalAmount = 900m, Status = PaymentBatchStatus.Pending, BankTransferReference = "PAY-SOD" });
        await db.SaveChangesAsync();
        return run.Id;
    }

    private async Task<RunActResult> RequestAsync(Guid runId, Guid by, string? reason = "رُحّل التشغيل لفترة خاطئة")
    {
        await using var db = Db();
        return await new RequestPayrollReversalHandler(new PayrollRepository(db))
            .Handle(new RequestPayrollReversalCommand(runId, by, reason), CancellationToken.None);
    }

    private async Task<RunActResult> DecideAsync(Guid requestId, Guid by, bool approve, string? notes = "ملاحظة القرار")
    {
        await using var db = Db();
        return await new DecidePayrollReversalHandler(new PayrollRepository(db), _events)
            .Handle(new DecidePayrollReversalCommand(requestId, by, approve, notes), CancellationToken.None);
    }

    private async Task<(PayrollRunStatus Run, PaymentBatchStatus Batch, PayrollReversalRequestStatus Request)> StateAsync(Guid runId)
    {
        await using var db = Db();
        return (
            (await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId)).Status,
            (await db.PaymentBatches.AsNoTracking().SingleAsync(b => b.PayrollRunId == runId)).Status,
            (await db.PayrollReversalRequests.AsNoTracking().OrderByDescending(r => r.RequestedAt).FirstAsync(r => r.PayrollRunId == runId)).Status);
    }

    private static readonly (PayrollRunStatus, PaymentBatchStatus, PayrollReversalRequestStatus) Untouched =
        (PayrollRunStatus.Posted, PaymentBatchStatus.Pending, PayrollReversalRequestStatus.Pending);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_requester_does_not_decide_his_own_request(bool approve)
    {
        var runId = await PostedRunAsync();
        var request = (await RequestAsync(runId, Reverser)).Id!.Value;

        var decided = await DecideAsync(request, Reverser, approve);

        Assert.Equal(RunActOutcome.Invalid, decided.Outcome);
        Assert.Equal(PayrollReversalMessages.DeciderIsRequester, decided.Error);
        Assert.Equal(Untouched, await StateAsync(runId));
        Assert.Empty(_events.Reversed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_poster_does_not_decide_the_reversal_of_the_run_he_posted(bool approve)
    {
        var runId = await PostedRunAsync();
        var request = (await RequestAsync(runId, Reverser)).Id!.Value;

        var decided = await DecideAsync(request, Poster, approve);

        Assert.Equal(RunActOutcome.Invalid, decided.Outcome);
        Assert.Equal(PayrollReversalMessages.DeciderIsPoster, decided.Error);
        Assert.Equal(Untouched, await StateAsync(runId));
        Assert.Empty(_events.Reversed);
    }

    [Fact]
    public async Task The_poster_may_ask_for_the_reversal_of_what_he_posted_and_another_person_decides()
    {
        var runId = await PostedRunAsync();

        var requested = await RequestAsync(runId, Poster);
        Assert.True(requested.Success, requested.Error);
        // He asked; he still does not decide (he is the requester and the poster).
        Assert.Equal(RunActOutcome.Invalid, (await DecideAsync(requested.Id!.Value, Poster, approve: true)).Outcome);
        Assert.Equal(Untouched, await StateAsync(runId));

        var decided = await DecideAsync(requested.Id!.Value, Approver, approve: true);

        Assert.True(decided.Success, decided.Error);
        Assert.Equal((PayrollRunStatus.Reversed, PaymentBatchStatus.Voided, PayrollReversalRequestStatus.Approved), await StateAsync(runId));
        Assert.Equal((runId, 900m, requested.Id!.Value), Assert.Single(_events.Reversed));
    }

    [Fact]
    public async Task A_third_person_decides_a_request_once()
    {
        var runId = await PostedRunAsync();
        var request = (await RequestAsync(runId, Reverser)).Id!.Value;

        Assert.True((await DecideAsync(request, Approver, approve: false, "لا موجب للعكس")).Success);

        Assert.Equal((PayrollRunStatus.Posted, PaymentBatchStatus.Pending, PayrollReversalRequestStatus.Rejected), await StateAsync(runId));
        var again = await DecideAsync(request, Approver, approve: true);
        Assert.Equal(RunActOutcome.Conflict, again.Outcome);
        Assert.Equal(PayrollReversalRequest.AlreadyDecided, again.Error);
        Assert.Equal(PayrollRunStatus.Posted, (await StateAsync(runId)).Run);
        Assert.Empty(_events.Reversed);
    }

    [Fact]
    public void A_request_is_decided_once_by_its_own_rule()
    {
        var request = new PayrollReversalRequest { PayrollRunId = Guid.NewGuid(), RequestedBy = Reverser, Reason = "سبب" };
        request.Decide(approve: false, Approver, PostedAt, "  ملاحظة  ");

        Assert.Equal((PayrollReversalRequestStatus.Rejected, (Guid?)Approver, "ملاحظة"), (request.Status, request.DecidedBy, request.DecisionNotes));
        Assert.Throws<InvalidOperationException>(() => request.Decide(approve: true, Approver, PostedAt, null));
        Assert.Equal(PayrollReversalRequestStatus.Rejected, request.Status);
    }

    // --- the event and its consumer ---

    private sealed record Reversed(Guid PayrollRunId, DateTime PeriodStart, DateTime PeriodEnd, decimal ReversedNetPay, Guid ReversalRequestId, DateTime ReversedAt)
        : IPayrollReversed;

    [Fact]
    public async Task The_consumer_records_the_reversal_once_with_the_amount_it_takes_back()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>().UseInMemoryDatabase($"pay056-{Guid.NewGuid():N}").Options;
        var message = new Reversed(Guid.NewGuid(), PostedAt.AddMonths(-1), PostedAt.AddDays(-2), 1_630_000m, Guid.NewGuid(), PostedAt);
        var context = new Mock<ConsumeContext<IPayrollReversed>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await using (var db = new AnalyticsDbContext(options))
            await new PayrollReversedConsumer(db, NullLogger<PayrollReversedConsumer>.Instance).Consume(context.Object);
        // The same message again (a redelivery): nothing is added.
        await using (var db = new AnalyticsDbContext(options))
            await new PayrollReversedConsumer(db, NullLogger<PayrollReversedConsumer>.Instance).Consume(context.Object);

        await using var read = new AnalyticsDbContext(options);
        var recorded = Assert.Single(await read.AnalyticsEvents.AsNoTracking().ToListAsync());
        Assert.Equal(PayrollReversedConsumer.EventName, recorded.EventName);
        Assert.Equal(PostedAt, recorded.OccurredAt);
        using var payload = JsonDocument.Parse(recorded.Payload!);
        Assert.Equal(message.PayrollRunId, payload.RootElement.GetProperty("PayrollRunId").GetGuid());
        Assert.Equal(1_630_000m, payload.RootElement.GetProperty("ReversedNetPay").GetDecimal());
        // The run's effect on a total of posted pay: what the posting added, taken back.
        Assert.Equal(-1_630_000m, payload.RootElement.GetProperty("NetEffect").GetDecimal());
        Assert.Equal(message.ReversalRequestId, payload.RootElement.GetProperty("ReversalRequestId").GetGuid());
    }

    [Fact]
    public void The_events_schema_is_version_1_and_names_no_person()
    {
        IPayrollReversed message = new Reversed(Guid.NewGuid(), PostedAt, PostedAt, 1m, Guid.NewGuid(), PostedAt);

        Assert.Equal(1, message.SchemaVersion);
        Assert.Null(message.TenantId);
        Assert.Equal(
            ["PayrollRunId", "PeriodEnd", "PeriodStart", "ReversalRequestId", "ReversedAt", "ReversedNetPay", "SchemaVersion", "TenantId"],
            typeof(IPayrollReversed).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
    }
}
