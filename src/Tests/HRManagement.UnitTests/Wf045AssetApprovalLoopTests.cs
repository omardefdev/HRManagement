using System.Net;
using System.Text;
using System.Text.Json;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Consumers;
using Admin.Logistics.Infrastructure.Abstractions;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Infrastructure.Integration;
using Admin.Logistics.Infrastructure.Services;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-45: the start, the retry and the withdrawal around a fake HR.Core.</summary>
public sealed class Wf045AssetApprovalLoopTests
{
    private const string Reason = "لم يعد التسليم مطلوباً";

    private static readonly Guid Requester = Guid.Parse("10000000-0000-4000-8000-000000000001");

    private static AdminLogisticsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AdminLogisticsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AssetApprovalLoop Loop(AdminLogisticsDbContext db, IAssetApprovalStarter starter, int maxAttempts = 30) =>
        new(db, starter, new NoEmployees(), new StaticOptions(new AssetApprovalOptions { Retry = { MaxAttempts = maxAttempts } }),
            NullLogger<AssetApprovalLoop>.Instance);

    private static async Task<AssetAssignment> StartingAssignmentAsync(AdminLogisticsDbContext db, Guid? instance = null)
    {
        var asset = new Asset { Name = "Laptop", TagNumber = "TAG-1", Status = AssetStatus.InStorage, IsPrivileged = true };
        var assignment = new AssetAssignment
        {
            AssetId = asset.Id,
            EmployeeId = Guid.NewGuid(),
            Status = instance is null ? AssetAssignmentStatus.ApprovalStarting : AssetAssignmentStatus.PendingApproval,
            WorkflowInstanceId = instance,
            CreatedBy = Requester
        };
        db.Assets.Add(asset);
        db.AssetAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment;
    }

    private static async Task<AssetLossReport> StartingLossAsync(AdminLogisticsDbContext db, Guid? instance = null)
    {
        var asset = new Asset { Name = "Laptop", TagNumber = "TAG-2", Status = AssetStatus.InStorage, IsPrivileged = true };
        var report = new AssetLossReport
        {
            AssetId = asset.Id,
            Status = instance is null ? AssetLossReportStatus.ApprovalStarting : AssetLossReportStatus.PendingApproval,
            WorkflowInstanceId = instance,
            CreatedBy = Requester
        };
        db.Assets.Add(asset);
        db.AssetLossReports.Add(report);
        await db.SaveChangesAsync();
        return report;
    }

    [Fact]
    public async Task A_started_request_is_linked_in_one_save()
    {
        await using var db = NewDb();
        var assignment = await StartingAssignmentAsync(db);
        var starter = new FakeStarter { Start = new AssetApprovalStartResult(true, Guid.NewGuid(), null, "InProgress") };

        var advance = await Loop(db, starter).AdvanceAsync(assignment);

        Assert.Equal(AssetApprovalAdvance.Pending, advance);
        var stored = await db.AssetAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(AssetAssignmentStatus.PendingApproval, stored.Status);
        Assert.Equal(starter.Start.InstanceId, stored.WorkflowInstanceId);
    }

    [Fact]
    public async Task A_failed_start_leaves_a_waiting_request_with_the_error_and_no_instance()
    {
        await using var db = NewDb();
        var assignment = await StartingAssignmentAsync(db);

        var advance = await Loop(db, new FakeStarter { Start = new AssetApprovalStartResult(false, null, "down") }).AdvanceAsync(assignment);

        Assert.Equal(AssetApprovalAdvance.Waiting, advance);
        var stored = await db.AssetAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(AssetAssignmentStatus.ApprovalStarting, stored.Status);
        Assert.Null(stored.WorkflowInstanceId);
        Assert.Equal(1, stored.ApprovalStartAttempts);
        Assert.Equal("down", stored.ApprovalStartLastError);
        Assert.NotNull(stored.ApprovalStartLastAttemptAt);
    }

    [Fact]
    public async Task The_last_attempt_hands_the_request_to_a_person()
    {
        await using var db = NewDb();
        var report = await StartingLossAsync(db);

        await Loop(db, new FakeStarter { Start = new AssetApprovalStartResult(false, null, "down") }, maxAttempts: 1).AdvanceAsync(report);

        Assert.True((await db.AssetLossReports.AsNoTracking().SingleAsync()).NeedsIntervention);
    }

    [Fact]
    public async Task A_start_that_finds_a_completed_instance_applies_its_outcome()
    {
        await using var db = NewDb();
        var assignment = await StartingAssignmentAsync(db);
        var instance = Guid.NewGuid();

        var advance = await Loop(db, new FakeStarter { Start = new AssetApprovalStartResult(true, instance, null, "Completed") }).AdvanceAsync(assignment);

        Assert.Equal(AssetApprovalAdvance.Finished, advance);
        var stored = await db.AssetAssignments.Include(a => a.Asset).AsNoTracking().SingleAsync();
        Assert.Equal(AssetAssignmentStatus.Active, stored.Status);
        Assert.Equal(instance, stored.WorkflowInstanceId);
        Assert.Equal(AssetStatus.InUse, stored.Asset.Status);
    }

    [Fact]
    public async Task A_start_that_finds_a_rejected_instance_frees_the_asset()
    {
        await using var db = NewDb();
        var assignment = await StartingAssignmentAsync(db);

        await Loop(db, new FakeStarter { Start = new AssetApprovalStartResult(true, Guid.NewGuid(), null, "Rejected") }).AdvanceAsync(assignment);

        var stored = await db.AssetAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(AssetAssignmentStatus.Rejected, stored.Status);
        Assert.NotNull(stored.ReturnedAt);
    }

    [Fact]
    public async Task A_start_that_finds_a_completed_loss_marks_the_asset_lost()
    {
        await using var db = NewDb();
        var report = await StartingLossAsync(db);

        await Loop(db, new FakeStarter { Start = new AssetApprovalStartResult(true, Guid.NewGuid(), null, "Completed") }).AdvanceAsync(report);

        var stored = await db.AssetLossReports.Include(r => r.Asset).AsNoTracking().SingleAsync();
        Assert.Equal(AssetLossReportStatus.Confirmed, stored.Status);
        Assert.Equal(AssetStatus.Lost, stored.Asset.Status);
        Assert.NotNull(stored.SettlementApprovedAt);
    }

    [Fact]
    public async Task A_start_that_finds_a_cancelled_instance_ends_the_request()
    {
        await using var db = NewDb();
        var assignment = await StartingAssignmentAsync(db);

        await Loop(db, new FakeStarter { Start = new AssetApprovalStartResult(true, Guid.NewGuid(), null, "Cancelled") }).AdvanceAsync(assignment);

        var stored = await db.AssetAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(AssetAssignmentStatus.Withdrawn, stored.Status);
        Assert.NotNull(stored.ReturnedAt);
    }

    [Fact]
    public async Task A_request_already_past_its_start_is_left_alone()
    {
        await using var db = NewDb();
        var assignment = await StartingAssignmentAsync(db, Guid.NewGuid());
        var starter = new FakeStarter();

        Assert.Equal(AssetApprovalAdvance.NotStarting, await Loop(db, starter).AdvanceAsync(assignment));
        Assert.Equal(0, starter.Starts);
    }

    [Fact]
    public async Task Withdrawing_cancels_in_hr_core_first()
    {
        await using var db = NewDb();
        var instance = Guid.NewGuid();
        var assignment = await StartingAssignmentAsync(db, instance);
        var starter = new FakeStarter { Cancel = new AssetApprovalCancelResult(AssetApprovalCancelOutcome.Cancelled) };

        var outcome = await Loop(db, starter).WithdrawAssignmentAsync(assignment.Id, Requester, Reason);

        Assert.Equal(AssetWithdrawOutcome.Withdrawn, outcome);
        Assert.Equal(instance, starter.CancelledInstance);
        Assert.Equal(Reason, starter.CancelledReason);
        var stored = await db.AssetAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(AssetAssignmentStatus.Withdrawn, stored.Status);
        Assert.Equal(Requester, stored.WithdrawnBy);
        Assert.NotNull(stored.ReturnedAt);
    }

    [Theory]
    [InlineData("Completed", AssetWithdrawOutcome.Approved, AssetAssignmentStatus.Active)]
    [InlineData("Rejected", AssetWithdrawOutcome.Rejected, AssetAssignmentStatus.Rejected)]
    [InlineData("Cancelled", AssetWithdrawOutcome.Withdrawn, AssetAssignmentStatus.Withdrawn)]
    public async Task A_finished_instance_decides_what_the_withdrawal_does(
        string status, AssetWithdrawOutcome expected, AssetAssignmentStatus ending)
    {
        await using var db = NewDb();
        var assignment = await StartingAssignmentAsync(db, Guid.NewGuid());
        var starter = new FakeStarter { Cancel = new AssetApprovalCancelResult(AssetApprovalCancelOutcome.Finished, status) };

        var outcome = await Loop(db, starter).WithdrawAssignmentAsync(assignment.Id, Requester, Reason);

        Assert.Equal(expected, outcome);
        Assert.Equal(ending, (await db.AssetAssignments.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData(AssetApprovalCancelOutcome.Conflict, AssetWithdrawOutcome.Conflict)]
    [InlineData(AssetApprovalCancelOutcome.Failed, AssetWithdrawOutcome.Failed)]
    public async Task A_withdrawal_hr_core_did_not_accept_changes_nothing(AssetApprovalCancelOutcome answer, AssetWithdrawOutcome expected)
    {
        await using var db = NewDb();
        var report = await StartingLossAsync(db, Guid.NewGuid());

        var outcome = await Loop(db, new FakeStarter { Cancel = new AssetApprovalCancelResult(answer) })
            .WithdrawLossAsync(report.Id, Requester, Reason);

        Assert.Equal(expected, outcome);
        var stored = await db.AssetLossReports.AsNoTracking().SingleAsync();
        Assert.Equal(AssetLossReportStatus.PendingApproval, stored.Status);
        Assert.Null(stored.WithdrawnAt);
    }

    [Fact]
    public async Task Withdrawing_a_waiting_request_cancels_the_instance_a_lost_answer_left_behind()
    {
        await using var db = NewDb();
        var assignment = await StartingAssignmentAsync(db);
        var left = Guid.NewGuid();
        var starter = new FakeStarter
        {
            Start = new AssetApprovalStartResult(true, left, null, "InProgress"),
            Cancel = new AssetApprovalCancelResult(AssetApprovalCancelOutcome.Cancelled)
        };

        var outcome = await Loop(db, starter).WithdrawAssignmentAsync(assignment.Id, Requester, Reason);

        Assert.Equal(AssetWithdrawOutcome.Withdrawn, outcome);
        Assert.Equal(left, starter.CancelledInstance);
    }

    [Fact]
    public async Task With_hr_core_down_a_waiting_request_can_still_be_withdrawn_and_the_asset_freed()
    {
        await using var db = NewDb();
        var assignment = await StartingAssignmentAsync(db);

        var outcome = await Loop(db, new FakeStarter { Start = new AssetApprovalStartResult(false, null, "down") })
            .WithdrawAssignmentAsync(assignment.Id, Requester, Reason);

        Assert.Equal(AssetWithdrawOutcome.Withdrawn, outcome);
        Assert.NotNull((await db.AssetAssignments.AsNoTracking().SingleAsync()).ReturnedAt);
    }

    [Fact]
    public async Task A_finished_request_cannot_be_withdrawn()
    {
        await using var db = NewDb();
        var assignment = await StartingAssignmentAsync(db);
        assignment.Status = AssetAssignmentStatus.Active;
        await db.SaveChangesAsync();

        Assert.Equal(AssetWithdrawOutcome.NotWaiting, await Loop(db, new FakeStarter()).WithdrawAssignmentAsync(assignment.Id, Requester, Reason));
    }

    // The starter.

    private static (AssetApprovalStarter Starter, Recording Handler, AdminLogisticsDbContext Db) NewStarter(Func<HttpResponseMessage>? answer = null)
    {
        var handler = new Recording(answer ?? (() => Json(HttpStatusCode.Created, new { id = Guid.NewGuid(), status = "InProgress", alreadyActive = true })));
        var db = NewDb();
        return (new AssetApprovalStarter(new SingleClient(handler), db, NullLogger<AssetApprovalStarter>.Instance), handler, db);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, object body) =>
        new(code) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task The_start_names_the_key_the_requester_the_subject_and_the_scope()
    {
        var (starter, handler, db) = NewStarter();
        await using var _ = db;
        var (employee, unit) = (Guid.NewGuid(), Guid.NewGuid());
        db.EmployeeOrgUnitSnapshots.Add(new EmployeeOrgUnitSnapshot { EmployeeId = employee, PrimaryOrgUnitId = unit });
        await db.SaveChangesAsync();
        var asset = new Asset { TagNumber = "12345678901", IsPrivileged = true, Value = 15000.5m };
        var assignment = new AssetAssignment { AssetId = asset.Id, EmployeeId = employee, CreatedBy = Requester, RequestedByName = "عمر" };

        var result = await starter.StartAsync(assignment, asset);

        Assert.True(result.Success);
        Assert.Equal("InProgress", result.InstanceStatus);
        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal($"AssetAssignment:{assignment.Id:D}", root.GetProperty("idempotencyKey").GetString());
        Assert.Equal(Requester, Assert.Single(root.GetProperty("excludedApproverUserIds").EnumerateArray()).GetGuid());
        Assert.Equal(employee, root.GetProperty("subjectEmployeeId").GetGuid());
        Assert.Equal(unit, root.GetProperty("orgUnitId").GetGuid());
        Assert.DoesNotContain("12345678901", root.GetProperty("displayTitle").GetString());
        Assert.StartsWith("/ui/admin-logistics/", root.GetProperty("detailsUrl").GetString());
        Assert.Equal(Requester.ToString("D"), handler.Header(ForwardActingUserHandler.HeaderName));
        Assert.Equal("عمر", ForwardActingUserHandler.DecodeName(handler.Header(ForwardActingUserHandler.NameHeaderName)));
    }

    [Fact]
    public async Task A_loss_with_no_holder_has_no_subject_and_no_unit()
    {
        var (starter, handler, db) = NewStarter();
        await using var _ = db;
        var asset = new Asset { TagNumber = "TAG-3", IsPrivileged = true };
        var report = new AssetLossReport { AssetId = asset.Id, CreatedBy = Requester, Reason = AssetLossReason.Theft };

        await starter.StartAsync(report, asset, null);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal($"AssetLossReport:{report.Id:D}", body.RootElement.GetProperty("idempotencyKey").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("subjectEmployeeId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("orgUnitId").ValueKind);
    }

    [Fact]
    public async Task The_start_never_throws()
    {
        var (starter, _, db) = NewStarter(() => throw new HttpRequestException("refused"));
        await using var __ = db;
        var asset = new Asset { TagNumber = "TAG-4" };

        var result = await starter.StartAsync(new AssetAssignment { AssetId = asset.Id }, asset);

        Assert.False(result.Success);
        Assert.Null(result.InstanceId);
        Assert.Equal(AssetApprovalStarter.UnavailableMessage, result.Error);
    }

    [Fact]
    public async Task A_refused_start_carries_the_arabic_reason()
    {
        var (starter, _, db) = NewStarter(() => Json(HttpStatusCode.UnprocessableEntity, new { error = "لا يوجد تعريف منشور" }));
        await using var __ = db;
        var asset = new Asset { TagNumber = "TAG-5" };

        Assert.Equal("لا يوجد تعريف منشور", (await starter.StartAsync(new AssetAssignment { AssetId = asset.Id }, asset)).Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, AssetApprovalCancelOutcome.Cancelled)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AssetApprovalCancelOutcome.Failed)]
    public async Task The_cancel_reads_the_answer(HttpStatusCode code, AssetApprovalCancelOutcome expected)
    {
        var (starter, handler, db) = NewStarter(() => Json(code, new { }));
        await using var _ = db;
        var instance = Guid.NewGuid();

        var result = await starter.CancelAsync(instance, Reason);

        Assert.Equal(expected, result.Outcome);
        Assert.EndsWith($"/api/workflow-instances/{instance:D}/cancel", handler.Uri!.AbsolutePath);
    }

    [Fact]
    public async Task A_409_with_a_status_is_a_finished_instance_and_without_one_a_race()
    {
        var (finished, _, db1) = NewStarter(() => Json(HttpStatusCode.Conflict, new { error = "منتهٍ", status = "Completed" }));
        var (raced, _, db2) = NewStarter(() => Json(HttpStatusCode.Conflict, new { error = "تعارض" }));
        await using var _1 = db1;
        await using var _2 = db2;

        var first = await finished.CancelAsync(Guid.NewGuid(), Reason);
        Assert.Equal(AssetApprovalCancelOutcome.Finished, first.Outcome);
        Assert.Equal("Completed", first.InstanceStatus);

        Assert.Equal(AssetApprovalCancelOutcome.Conflict, (await raced.CancelAsync(Guid.NewGuid(), Reason)).Outcome);
    }

    // The job.

    private static ServiceProvider JobServices(string dbName, IAssetApprovalStarter starter, int intervalSeconds = 1)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddTenantContext();
        services.AddDbContext<AdminLogisticsDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton(starter);
        services.AddSingleton<IEmployeeLookup, NoEmployees>();
        services.AddSingleton<IOptionsMonitor<AssetApprovalOptions>>(new StaticOptions(new AssetApprovalOptions { Retry = { IntervalSeconds = intervalSeconds } }));
        services.AddScoped<AssetApprovalLoop>();
        services.AddSingleton<AssetApprovalStartRetryJob>();
        return services.BuildServiceProvider();
    }

    private static async Task SeedTenantRequestAsync(ServiceProvider provider, Guid tenant, Guid? createdBy, DateTime? lastAttempt = null)
    {
        TenantIdFlow.Set(tenant);
        try
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AdminLogisticsDbContext>();
            var asset = new Asset { TenantId = tenant, Name = "Laptop", TagNumber = $"T-{Guid.NewGuid():N}", Status = AssetStatus.InStorage };
            var assignment = new AssetAssignment
            {
                TenantId = tenant,
                AssetId = asset.Id,
                EmployeeId = Guid.NewGuid(),
                Status = AssetAssignmentStatus.ApprovalStarting,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                ApprovalStartLastAttemptAt = lastAttempt
            };
            db.Assets.Add(asset);
            db.AssetAssignments.Add(assignment);
            db.AuditLogs.Add(new EntityAuditLog { TenantId = tenant, EntityType = nameof(AssetAssignment), EntityId = assignment.Id, Action = "Created" });
            await db.SaveChangesAsync();
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private static async Task<AssetAssignment> ReadAsync(ServiceProvider provider, Guid tenant)
    {
        TenantIdFlow.Set(tenant);
        try
        {
            using var scope = provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AdminLogisticsDbContext>().AssetAssignments.AsNoTracking().SingleAsync();
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    [Fact]
    public async Task The_job_starts_each_request_in_its_tenant_and_as_its_requester()
    {
        var (tenantA, tenantB, userB) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var starter = new FakeStarter { StartEach = () => new AssetApprovalStartResult(true, Guid.NewGuid(), null, "InProgress") };
        await using var provider = JobServices(Guid.NewGuid().ToString(), starter);
        await SeedTenantRequestAsync(provider, tenantA, Requester);
        await SeedTenantRequestAsync(provider, tenantB, userB);

        await provider.GetRequiredService<AssetApprovalStartRetryJob>().RunOnceAsync();

        Assert.Equal(2, starter.Seen.Count);
        Assert.Contains((tenantA, Requester), starter.Seen);
        Assert.Contains((tenantB, userB), starter.Seen);
        Assert.Equal(AssetAssignmentStatus.PendingApproval, (await ReadAsync(provider, tenantA)).Status);
        Assert.Equal(AssetAssignmentStatus.PendingApproval, (await ReadAsync(provider, tenantB)).Status);
        Assert.Null(TenantIdFlow.Get());
        Assert.Null(ActingUserScope.Current);
    }

    [Fact]
    public async Task The_job_waits_for_the_interval_after_the_last_attempt()
    {
        var tenant = Guid.NewGuid();
        var starter = new FakeStarter();
        await using var provider = JobServices(Guid.NewGuid().ToString(), starter, intervalSeconds: 3600);
        await SeedTenantRequestAsync(provider, tenant, Requester, lastAttempt: DateTime.UtcNow);

        await provider.GetRequiredService<AssetApprovalStartRetryJob>().RunOnceAsync();

        Assert.Equal(0, starter.Starts);
    }

    [Fact]
    public async Task A_request_nobody_is_known_to_have_made_goes_to_a_person()
    {
        var tenant = Guid.NewGuid();
        var starter = new FakeStarter();
        await using var provider = JobServices(Guid.NewGuid().ToString(), starter);
        await SeedTenantRequestAsync(provider, tenant, createdBy: null);

        await provider.GetRequiredService<AssetApprovalStartRetryJob>().RunOnceAsync();

        Assert.Equal(0, starter.Starts);
        var stored = await ReadAsync(provider, tenant);
        Assert.True(stored.NeedsIntervention);
        Assert.Equal(AssetApprovalLoop.UnknownRequesterError, stored.ApprovalStartLastError);
    }

    [Fact]
    public async Task The_audit_log_names_the_tenants_that_made_requests_and_nothing_else()
    {
        await using var db = NewDb();
        var (assignments, losses) = (Guid.NewGuid(), Guid.NewGuid());
        db.AuditLogs.AddRange(
            new EntityAuditLog { TenantId = assignments, EntityType = nameof(AssetAssignment), Action = "Created" },
            new EntityAuditLog { TenantId = assignments, EntityType = nameof(AssetAssignment), Action = "Updated" },
            new EntityAuditLog { TenantId = losses, EntityType = nameof(AssetLossReport), Action = "Created" },
            new EntityAuditLog { TenantId = Guid.NewGuid(), EntityType = nameof(Asset), Action = "Created" },
            new EntityAuditLog { TenantId = Guid.Empty, EntityType = nameof(AssetAssignment), Action = "Created" });
        await db.SaveChangesAsync();

        var tenants = await AssetApprovalStartRetryJob.FindTenantsAsync(db);

        Assert.Equal(2, tenants.Count);
        Assert.Contains(assignments, tenants);
        Assert.Contains(losses, tenants);
    }

    // The separation saga.

    private sealed class Compensating(AdminLogisticsDbContext db) : SeparationPartyAckConsumer(db)
    {
        public Task<string?> RunExecuteAsync(ConsumeContext<IExecuteSeparationParty> context) => ExecuteAsync(context);

        public Task<string?> RunCompensateAsync(ConsumeContext<IExecuteSeparationParty> context) => CompensateAsync(context);
    }

    private static ConsumeContext<IExecuteSeparationParty> SeparationOf(Guid employee)
    {
        var message = new Mock<IExecuteSeparationParty>();
        message.SetupGet(m => m.EmployeeId).Returns(employee);
        var context = new Mock<ConsumeContext<IExecuteSeparationParty>>();
        context.SetupGet(c => c.Message).Returns(message.Object);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    [Fact]
    public async Task Compensation_reopens_what_the_saga_closed_and_nothing_someone_else_closed()
    {
        await using var db = NewDb();
        var employee = Guid.NewGuid();
        var now = DateTime.UtcNow;
        Asset Stored(string tag, AssetStatus status) => new() { Name = "Laptop", TagNumber = tag, Status = status };
        var (held, withdrawn, rejected, returned) = (Stored("H", AssetStatus.InUse), Stored("W", AssetStatus.InStorage), Stored("R", AssetStatus.InStorage), Stored("B", AssetStatus.InStorage));
        db.Assets.AddRange(held, withdrawn, rejected, returned);
        var active = new AssetAssignment { AssetId = held.Id, EmployeeId = employee, Status = AssetAssignmentStatus.Active };
        var closed = new[]
        {
            new AssetAssignment { AssetId = withdrawn.Id, EmployeeId = employee, Status = AssetAssignmentStatus.Withdrawn, ReturnedAt = now },
            new AssetAssignment { AssetId = rejected.Id, EmployeeId = employee, Status = AssetAssignmentStatus.Rejected, ReturnedAt = now },
            new AssetAssignment { AssetId = returned.Id, EmployeeId = employee, Status = AssetAssignmentStatus.Returned, ReturnedAt = now }
        };
        db.AssetAssignments.Add(active);
        db.AssetAssignments.AddRange(closed);
        await db.SaveChangesAsync();
        var consumer = new Compensating(db);
        var context = SeparationOf(employee);

        await consumer.RunExecuteAsync(context);
        Assert.NotNull(active.ReturnedAt);
        Assert.Equal(AssetStatus.InStorage, held.Status);

        await consumer.RunCompensateAsync(context);

        Assert.Null(active.ReturnedAt);
        Assert.Equal(AssetStatus.InUse, held.Status);
        Assert.All(closed, a => Assert.NotNull(a.ReturnedAt));
        Assert.Equal(AssetStatus.InStorage, withdrawn.Status);
    }

    private sealed class FakeStarter : IAssetApprovalStarter
    {
        public AssetApprovalStartResult Start { get; init; } = new(false, null, "not expected");

        public Func<AssetApprovalStartResult>? StartEach { get; init; }

        public AssetApprovalCancelResult Cancel { get; init; } = new(AssetApprovalCancelOutcome.Failed);

        public int Starts { get; private set; }

        public List<(Guid? Tenant, Guid? Acting)> Seen { get; } = [];

        public Guid? CancelledInstance { get; private set; }

        public string? CancelledReason { get; private set; }

        public Task<AssetApprovalStartResult> StartAsync(AssetAssignment assignment, Asset asset, CancellationToken ct = default) => Answer();

        public Task<AssetApprovalStartResult> StartAsync(AssetLossReport report, Asset asset, Guid? holderEmployeeId, CancellationToken ct = default) => Answer();

        public Task<AssetApprovalCancelResult> CancelAsync(Guid instanceId, string reason, CancellationToken ct = default)
        {
            CancelledInstance = instanceId;
            CancelledReason = reason;
            return Task.FromResult(Cancel);
        }

        private Task<AssetApprovalStartResult> Answer()
        {
            Starts++;
            lock (Seen)
                Seen.Add((TenantIdFlow.Get(), ActingUserScope.Current?.UserId));
            return Task.FromResult(StartEach?.Invoke() ?? Start);
        }
    }

    private sealed class NoEmployees : IEmployeeLookup
    {
        public Task<string?> GetEmployeeDisplayNameAsync(Guid employeeId, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task<IReadOnlyDictionary<Guid, string>> GetEmployeeDisplayNamesAsync(IEnumerable<Guid> employeeIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class StaticOptions(AssetApprovalOptions value) : IOptionsMonitor<AssetApprovalOptions>
    {
        public AssetApprovalOptions CurrentValue => value;

        public AssetApprovalOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AssetApprovalOptions, string?> listener) => null;
    }

    private sealed class SingleClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://hr-core-web:8080/") };
    }

    private sealed class Recording(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        private HttpRequestMessage? _request;

        public string? Body { get; private set; }

        public Uri? Uri => _request?.RequestUri;

        public string? Header(string name) => _request!.Headers.TryGetValues(name, out var values) ? values.Single() : null;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return answer();
        }
    }
}
