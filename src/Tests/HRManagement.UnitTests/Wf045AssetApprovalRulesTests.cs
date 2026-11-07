using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>PLAN-45: the moves of a request for approval.</summary>
public sealed class Wf045AssetApprovalRulesTests
{
    private const string Reason = "لم يعد التسليم مطلوباً";

    [Theory]
    [InlineData(AssetAssignmentStatus.ApprovalStarting, AssetAssignmentStatus.PendingApproval, true)]
    [InlineData(AssetAssignmentStatus.ApprovalStarting, AssetAssignmentStatus.Withdrawn, true)]
    [InlineData(AssetAssignmentStatus.PendingApproval, AssetAssignmentStatus.Active, true)]
    [InlineData(AssetAssignmentStatus.PendingApproval, AssetAssignmentStatus.Rejected, true)]
    [InlineData(AssetAssignmentStatus.PendingApproval, AssetAssignmentStatus.Withdrawn, true)]
    [InlineData(AssetAssignmentStatus.ApprovalStarting, AssetAssignmentStatus.Active, false)]
    [InlineData(AssetAssignmentStatus.Withdrawn, AssetAssignmentStatus.Active, false)]
    [InlineData(AssetAssignmentStatus.Withdrawn, AssetAssignmentStatus.PendingApproval, false)]
    [InlineData(AssetAssignmentStatus.Active, AssetAssignmentStatus.Withdrawn, false)]
    [InlineData(AssetAssignmentStatus.Rejected, AssetAssignmentStatus.Withdrawn, false)]
    public void Assignment_moves(AssetAssignmentStatus from, AssetAssignmentStatus to, bool allowed) =>
        Assert.Equal(allowed, AssetApprovalRules.CanMove(from, to));

    [Theory]
    [InlineData(AssetLossReportStatus.ApprovalStarting, AssetLossReportStatus.PendingApproval, true)]
    [InlineData(AssetLossReportStatus.ApprovalStarting, AssetLossReportStatus.Withdrawn, true)]
    [InlineData(AssetLossReportStatus.PendingApproval, AssetLossReportStatus.Confirmed, true)]
    [InlineData(AssetLossReportStatus.PendingApproval, AssetLossReportStatus.Rejected, true)]
    [InlineData(AssetLossReportStatus.PendingApproval, AssetLossReportStatus.Withdrawn, true)]
    [InlineData(AssetLossReportStatus.ApprovalStarting, AssetLossReportStatus.Confirmed, false)]
    [InlineData(AssetLossReportStatus.Withdrawn, AssetLossReportStatus.Confirmed, false)]
    [InlineData(AssetLossReportStatus.Confirmed, AssetLossReportStatus.Withdrawn, false)]
    public void Loss_moves(AssetLossReportStatus from, AssetLossReportStatus to, bool allowed) =>
        Assert.Equal(allowed, AssetApprovalRules.CanMove(from, to));

    [Fact]
    public void New_values_sit_at_the_end_because_the_status_is_stored_as_an_integer()
    {
        Assert.Equal(4, (int)AssetAssignmentStatus.ApprovalStarting);
        Assert.Equal(5, (int)AssetAssignmentStatus.Withdrawn);
        Assert.Equal(3, (int)AssetAssignmentStatus.Rejected);
        Assert.Equal(4, (int)AssetLossReportStatus.ApprovalStarting);
        Assert.Equal(5, (int)AssetLossReportStatus.Withdrawn);
        Assert.Equal(3, (int)AssetLossReportStatus.Recovered);
    }

    [Fact]
    public void A_new_report_starts_as_a_request_with_no_approval_yet() =>
        Assert.Equal(AssetLossReportStatus.ApprovalStarting, new AssetLossReport().Status);

    [Fact]
    public void Linking_puts_the_status_and_the_instance_together_and_clears_the_failure()
    {
        var instance = Guid.NewGuid();
        var assignment = new AssetAssignment
        {
            Status = AssetAssignmentStatus.ApprovalStarting,
            ApprovalStartLastError = "down",
            NeedsIntervention = true
        };

        AssetApprovalRules.LinkPending(assignment, instance);

        Assert.Equal(AssetAssignmentStatus.PendingApproval, assignment.Status);
        Assert.Equal(instance, assignment.WorkflowInstanceId);
        Assert.Null(assignment.ApprovalStartLastError);
        Assert.False(assignment.NeedsIntervention);
    }

    [Fact]
    public void A_request_cannot_be_pending_without_an_instance() =>
        Assert.Throws<ArgumentException>(() =>
            AssetApprovalRules.LinkPending(new AssetLossReport { Status = AssetLossReportStatus.ApprovalStarting }, Guid.Empty));

    [Fact]
    public void Only_a_request_waiting_for_its_start_can_be_linked() =>
        Assert.Throws<InvalidOperationException>(() =>
            AssetApprovalRules.LinkPending(new AssetAssignment { Status = AssetAssignmentStatus.Withdrawn }, Guid.NewGuid()));

    [Theory]
    [InlineData(AssetAssignmentStatus.ApprovalStarting)]
    [InlineData(AssetAssignmentStatus.PendingApproval)]
    public void Withdrawing_frees_the_asset_and_records_who_and_why(AssetAssignmentStatus from)
    {
        var actor = Guid.NewGuid();
        var now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var asset = new Asset { Status = AssetStatus.InStorage };
        var assignment = new AssetAssignment { AssetId = asset.Id, Status = from };

        Assert.False(AssetAssignmentRules.CanAssign(asset, [assignment]));

        AssetApprovalRules.Withdraw(assignment, actor, $"  {Reason} ", now);

        Assert.Equal(AssetAssignmentStatus.Withdrawn, assignment.Status);
        Assert.Equal(now, assignment.ReturnedAt);
        Assert.Equal(now, assignment.WithdrawnAt);
        Assert.Equal(actor, assignment.WithdrawnBy);
        Assert.Equal(Reason, assignment.WithdrawnReason);
        Assert.True(AssetAssignmentRules.CanAssign(asset, [assignment]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("قصير")]
    [InlineData("          ")]
    public void A_withdrawal_needs_a_reason_of_ten_characters(string? reason)
    {
        Assert.False(AssetApprovalRules.IsValidWithdrawReason(reason));
        Assert.Throws<ArgumentException>(() =>
            AssetApprovalRules.Withdraw(new AssetLossReport { Status = AssetLossReportStatus.PendingApproval }, null, reason!, DateTime.UtcNow));
    }

    [Fact]
    public void A_finished_request_cannot_be_withdrawn() =>
        Assert.Throws<InvalidOperationException>(() =>
            AssetApprovalRules.Withdraw(new AssetAssignment { Status = AssetAssignmentStatus.Active }, null, Reason, DateTime.UtcNow));

    [Fact]
    public void A_request_waiting_to_start_holds_the_asset_as_a_pending_one_does()
    {
        var asset = new Asset { Status = AssetStatus.InStorage };
        var waiting = new AssetAssignment { AssetId = asset.Id, Status = AssetAssignmentStatus.ApprovalStarting };

        Assert.True(AssetAssignmentRules.HasOpenAssignment([waiting], asset.Id));
        Assert.False(AssetAssignmentRules.CanAssign(asset, [waiting]));
    }

    [Fact]
    public void Failures_add_up_and_the_limit_asks_for_a_person_once()
    {
        var now = DateTime.UtcNow;
        var request = new AssetAssignment { Status = AssetAssignmentStatus.ApprovalStarting };

        Assert.False(AssetApprovalRules.RecordStartFailure(request, new string('x', 700), now, maxAttempts: 2));
        Assert.Equal(AssetApprovalRules.MaxStartErrorLength, request.ApprovalStartLastError!.Length);
        Assert.False(request.NeedsIntervention);

        Assert.True(AssetApprovalRules.RecordStartFailure(request, "down", now, maxAttempts: 2));
        Assert.True(request.NeedsIntervention);
        Assert.Equal(2, request.ApprovalStartAttempts);
        Assert.Equal(now, request.ApprovalStartLastAttemptAt);

        Assert.False(AssetApprovalRules.RecordStartFailure(request, "down", now, maxAttempts: 2));
    }

    [Fact]
    public void An_approved_assignment_is_refused_when_the_asset_is_no_longer_free()
    {
        var asset = new Asset { Status = AssetStatus.Lost };
        var assignment = new AssetAssignment { AssetId = asset.Id, Status = AssetAssignmentStatus.PendingApproval };

        AssetApprovalRules.ApplyApproved(assignment, asset, []);

        Assert.Equal(AssetAssignmentStatus.Rejected, assignment.Status);
        Assert.NotNull(assignment.ReturnedAt);
        Assert.Equal(AssetStatus.Lost, asset.Status);
    }

    [Fact]
    public void A_long_reason_fits_the_columns_so_a_rejection_never_fails_its_consumer()
    {
        var assignment = new AssetAssignment { Status = AssetAssignmentStatus.PendingApproval };
        var report = new AssetLossReport { Status = AssetLossReportStatus.PendingApproval, Notes = "ملاحظة" };
        var reason = new string('س', 2000);

        AssetApprovalRules.ApplyRejected(assignment, reason, DateTime.UtcNow);
        AssetApprovalRules.ApplyRejected(report, reason);

        Assert.Equal(256, assignment.EvidenceRef!.Length);
        Assert.Equal(256, report.Notes!.Length);
    }
}
