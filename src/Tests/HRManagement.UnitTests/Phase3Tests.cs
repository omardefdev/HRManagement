using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;
using HR.Movements.Domain.Services;
using HR.Movements.Infrastructure.Data;
using HR.Training.Domain.Entities;
using HR.Training.Domain.Enums;
using HR.Training.Domain.Services;
using HR.Training.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HRManagement.UnitTests;

public class Phase3Tests
{
    // ── HR.Movements: Transfer Workflow Rules ──

    [Fact]
    public void CanApprove_WhenPending_ReturnsTrue()
    {
        var tr = new TransferRequest { Status = ApprovalStatus.Pending };
        Assert.True(TransferWorkflowRules.CanApprove(tr));
    }

    [Theory]
    [InlineData(ApprovalStatus.Approved)]
    [InlineData(ApprovalStatus.Rejected)]
    [InlineData(ApprovalStatus.Executed)]
    public void CanApprove_WhenNotPending_ReturnsFalse(ApprovalStatus status)
    {
        var tr = new TransferRequest { Status = status };
        Assert.False(TransferWorkflowRules.CanApprove(tr));
    }

    [Fact]
    public void CanExecute_WhenApproved_ReturnsTrue()
    {
        var tr = new TransferRequest { Status = ApprovalStatus.Approved };
        Assert.True(TransferWorkflowRules.CanExecute(tr));
    }

    [Theory]
    [InlineData(ApprovalStatus.Pending)]
    [InlineData(ApprovalStatus.Rejected)]
    [InlineData(ApprovalStatus.Executed)]
    public void CanExecute_WhenNotApproved_ReturnsFalse(ApprovalStatus status)
    {
        var tr = new TransferRequest { Status = status };
        Assert.False(TransferWorkflowRules.CanExecute(tr));
    }

    [Fact]
    public void CanEdit_WhenPending_ReturnsTrue()
    {
        var tr = new TransferRequest { Status = ApprovalStatus.Pending };
        Assert.True(TransferWorkflowRules.CanEdit(tr));
    }

    [Fact]
    public void CanEdit_WhenExecuted_ReturnsFalse()
    {
        var tr = new TransferRequest { Status = ApprovalStatus.Executed };
        Assert.False(TransferWorkflowRules.CanEdit(tr));
    }

    [Fact]
    public void IsExecuted_WhenExecuted_ReturnsTrue()
    {
        var tr = new TransferRequest { Status = ApprovalStatus.Executed };
        Assert.True(TransferWorkflowRules.IsExecuted(tr));
    }

    [Fact]
    public void IsExecuted_WhenApproved_ReturnsFalse()
    {
        var tr = new TransferRequest { Status = ApprovalStatus.Approved };
        Assert.False(TransferWorkflowRules.IsExecuted(tr));
    }

    // ── HR.Movements: Secondment Lifecycle ──

    [Fact]
    public void SecondmentStatus_HasActive()
    {
        Assert.True(Enum.IsDefined(typeof(SecondmentStatus), SecondmentStatus.Active));
    }

    [Fact]
    public void SecondmentStatus_HasCorrectOrder()
    {
        Assert.Equal(0, (int)SecondmentStatus.Pending);
        Assert.Equal(1, (int)SecondmentStatus.Approved);
        Assert.Equal(2, (int)SecondmentStatus.Active);
        Assert.Equal(3, (int)SecondmentStatus.Closed);
    }

    [Fact]
    public void SecondmentRequest_DefaultsToCollectionEmpty()
    {
        var sr = new SecondmentRequest();
        Assert.Empty(sr.ExecutionRecords);
    }

    [Fact]
    public void MovementExecutionRecord_CanReferenceSecondment()
    {
        var rec = new MovementExecutionRecord
        {
            SecondmentRequestId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ExecutedAt = DateTime.UtcNow
        };
        Assert.NotNull(rec.SecondmentRequestId);
        Assert.Null(rec.TransferRequestId);
    }

    // ── HR.Movements: Financial Impact ──

    [Fact]
    public void TransferFinancialImpact_DefaultDecimalZero()
    {
        var fi = new TransferFinancialImpact();
        Assert.Equal(0m, fi.SalaryChange);
        Assert.Equal(0m, fi.AllowanceChange);
    }

    [Fact]
    public void TransferRequest_HasFinancialImpactsCollection()
    {
        var tr = new TransferRequest();
        Assert.NotNull(tr.FinancialImpacts);
        Assert.Empty(tr.FinancialImpacts);
    }

    // ── HR.Movements: DistributionPlan Status ──

    [Fact]
    public void DistributionPlanStatus_HasCorrectValues()
    {
        Assert.Equal(0, (int)DistributionPlanStatus.Draft);
        Assert.Equal(1, (int)DistributionPlanStatus.Approved);
        Assert.Equal(2, (int)DistributionPlanStatus.Active);
    }

    [Fact]
    public void DistributionPlan_HasStatusProperty()
    {
        var dp = new DistributionPlan { Status = DistributionPlanStatus.Draft };
        Assert.Equal(DistributionPlanStatus.Draft, dp.Status);
    }

    // ── HR.Training: Gap Analysis ──

    [Fact]
    public void GapAnalysis_EmptyRequired_Returns100Percent()
    {
        var result = TrainingGapAnalysis.AnalyzeGap([], null);
        Assert.Equal(100, result.CompletionPercentage);
        Assert.Empty(result.MissingCourseIds);
    }

    [Fact]
    public void GapAnalysis_AllCompleted_Returns100Percent()
    {
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        var json = $"[\"{c1}\",\"{c2}\"]";
        var result = TrainingGapAnalysis.AnalyzeGap([c1, c2], json);
        Assert.Equal(100, result.CompletionPercentage);
        Assert.Empty(result.MissingCourseIds);
        Assert.Equal(2, result.CompletedCourseIds.Count);
    }

    [Fact]
    public void GapAnalysis_NoneCompleted_Returns0Percent()
    {
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        var json = $"[\"{c1}\",\"{c2}\"]";
        var result = TrainingGapAnalysis.AnalyzeGap([], json);
        Assert.Equal(0, result.CompletionPercentage);
        Assert.Equal(2, result.MissingCourseIds.Count);
    }

    [Fact]
    public void GapAnalysis_PartialCompletion_ReturnsCorrectPercentage()
    {
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        var c3 = Guid.NewGuid();
        var json = $"[\"{c1}\",\"{c2}\",\"{c3}\"]";
        var result = TrainingGapAnalysis.AnalyzeGap([c1], json);
        Assert.Equal(33.3m, result.CompletionPercentage);
        Assert.Equal(2, result.MissingCourseIds.Count);
        Assert.Single(result.CompletedCourseIds);
    }

    [Fact]
    public void GapAnalysis_InvalidJson_ReturnsEmptyRequired()
    {
        var result = TrainingGapAnalysis.AnalyzeGap([Guid.NewGuid()], "not-json");
        Assert.Empty(result.RequiredCourseIds);
        Assert.Equal(100, result.CompletionPercentage);
    }

    // ── HR.Training: Nomination Workflow ──

    [Fact]
    public void TrainingNomination_DefaultStatus_CanBePending()
    {
        var n = new TrainingNomination { Status = ApprovalStatus.Pending };
        Assert.Equal(ApprovalStatus.Pending, n.Status);
    }

    [Fact]
    public void TrainingNomination_HasCourseFK()
    {
        var courseId = Guid.NewGuid();
        var n = new TrainingNomination { CourseId = courseId };
        Assert.Equal(courseId, n.CourseId);
    }

    // ── HR.Training: Mission Lifecycle ──

    [Fact]
    public void MissionStatus_HasCorrectValues()
    {
        Assert.Equal(0, (int)MissionStatus.Proposed);
        Assert.Equal(1, (int)MissionStatus.Approved);
        Assert.Equal(2, (int)MissionStatus.Ongoing);
        Assert.Equal(3, (int)MissionStatus.Completed);
    }

    [Fact]
    public void MissionFile_HasRequiredProperties()
    {
        var m = new MissionFile
        {
            EmployeeId = Guid.NewGuid(),
            Destination = "لندن",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = MissionStatus.Proposed
        };
        Assert.Equal("لندن", m.Destination);
        Assert.Equal(MissionStatus.Proposed, m.Status);
    }

    // ── Event Contracts ──

    [Fact]
    public void ITransferExecuted_SchemaVersion_Is2()
    {
        ITransferExecuted msg = new TransferExecutedStub
        {
            EmployeeId = Guid.NewGuid(),
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = Guid.NewGuid(),
            ExecutedAt = DateTime.UtcNow
        };
        Assert.Equal(2, msg.SchemaVersion);
    }

    [Fact]
    public void ITransferExecuted_V2_HasFinancialFields()
    {
        var msg = new TransferExecutedStub
        {
            EmployeeId = Guid.NewGuid(),
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = Guid.NewGuid(),
            ExecutedAt = DateTime.UtcNow,
            SalaryChange = 500m,
            AllowanceChange = 200m,
            TransferRequestId = Guid.NewGuid()
        };
        Assert.Equal(500m, msg.SalaryChange);
        Assert.Equal(200m, msg.AllowanceChange);
        Assert.NotNull(msg.TransferRequestId);
    }

    [Fact]
    public void ITrainingCompleted_SchemaVersion_Is3()
    {
        ITrainingCompleted msg = new TrainingCompletedStub
        {
            EmployeeId = Guid.NewGuid(),
            CourseId = Guid.NewGuid(),
            Score = 95,
            CertificateFileId = Guid.NewGuid(),
            CompletedAt = DateTime.UtcNow
        };
        Assert.Equal(3, msg.SchemaVersion);
    }

    [Fact]
    public void ITrainingCompleted_V3_HasExtendedFields()
    {
        var msg = new TrainingCompletedStub
        {
            EmployeeId = Guid.NewGuid(),
            CourseId = Guid.NewGuid(),
            Score = 88,
            CertificateFileId = null,
            CompletedAt = DateTime.UtcNow,
            CourseTitleAr = "إدارة مشاريع",
            CourseCode = "PM-101",
            DurationDays = 5,
            SourceSystem = "HR.Training"
        };
        Assert.Equal("إدارة مشاريع", msg.CourseTitleAr);
        Assert.Equal("PM-101", msg.CourseCode);
        Assert.Equal(5, msg.DurationDays);
    }

    [Fact]
    public void ISecondmentExecuted_Contract_IsUsable()
    {
        ISecondmentExecuted msg = new SecondmentExecutedStub
        {
            SecondmentRequestId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            HostOrg = "وزارة الصحة",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddMonths(6),
            CorrelationId = Guid.NewGuid(),
            SourceSystem = "HR.Movements"
        };
        Assert.Equal(1, msg.SchemaVersion);
        Assert.Equal("وزارة الصحة", msg.HostOrg);
    }

    [Fact]
    public void IMissionApproved_Contract_IsUsable()
    {
        IMissionApproved msg = new MissionApprovedStub
        {
            MissionFileId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Destination = "لندن",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddYears(1),
            CorrelationId = Guid.NewGuid(),
            SourceSystem = "HR.Training"
        };
        Assert.Equal(1, msg.SchemaVersion);
        Assert.Equal("لندن", msg.Destination);
    }

    // ── Stub Records ──

    private record TransferExecutedStub : ITransferExecuted
    {
        public Guid EmployeeId { get; init; }
        public Guid FromOrgUnitId { get; init; }
        public Guid ToOrgUnitId { get; init; }
        public DateTime ExecutedAt { get; init; }
        public Guid? TransferRequestId { get; init; }
        public decimal? SalaryChange { get; init; }
        public decimal? AllowanceChange { get; init; }
        public Guid? CorrelationId { get; init; }
        public string? SourceSystem { get; init; }
    }

    private record TrainingCompletedStub : ITrainingCompleted
    {
        public Guid EmployeeId { get; init; }
        public Guid CourseId { get; init; }
        public decimal Score { get; init; }
        public Guid? CertificateFileId { get; init; }
        public DateTime CompletedAt { get; init; }
        public string? CourseTitleAr { get; init; }
        public string? CourseCode { get; init; }
        public Guid? ProviderId { get; init; }
        public int? DurationDays { get; init; }
        public Guid? CorrelationId { get; init; }
        public string? SourceSystem { get; init; }
    }

    private record SecondmentExecutedStub : ISecondmentExecuted
    {
        public Guid SecondmentRequestId { get; init; }
        public Guid EmployeeId { get; init; }
        public string HostOrg { get; init; } = string.Empty;
        public DateTime StartDate { get; init; }
        public DateTime EndDate { get; init; }
        public Guid CorrelationId { get; init; }
        public string SourceSystem { get; init; } = string.Empty;
    }

    private record MissionApprovedStub : IMissionApproved
    {
        public Guid MissionFileId { get; init; }
        public Guid EmployeeId { get; init; }
        public string Destination { get; init; } = string.Empty;
        public DateTime StartDate { get; init; }
        public DateTime EndDate { get; init; }
        public Guid CorrelationId { get; init; }
        public string SourceSystem { get; init; } = string.Empty;
    }
}
