using System.Text.Json;
using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;
using HR.Movements.Domain.Services;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.SeparationOfDuties;

namespace HRManagement.UnitTests;

public class Fun003JourneyMatrixTests
{
    private static string ResolveMatrixPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "fun-movements", "journey-matrix.json");
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("docs/fun-movements/journey-matrix.json");
    }

    [Fact]
    public void Matrix_includes_transfer_secondment_loan()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(ResolveMatrixPath()));
        var kinds = doc.RootElement.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Transfer", kinds);
        Assert.Contains("Secondment", kinds);
        Assert.Contains("Loan", kinds);
    }
}

public class Fun003ChecklistGovernanceTests
{
    [Fact]
    public void Defaults_set_owner_and_sla_for_transfer()
    {
        var item = MovementChecklistDefaults.CreateFor(MovementKind.Transfer, Guid.NewGuid())
            .First(c => c.Step == MovementChecklistStep.FinancialReviewed);
        Assert.Equal("payroll", item.OwnerRole);
        Assert.Equal(48, item.SlaHours);
        Assert.True(item.IsRequired);
    }

    [Fact]
    public void SoftCancel_transfer_retains_flags()
    {
        var t = new TransferRequest
        {
            EmployeeId = Guid.NewGuid(),
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = Guid.NewGuid(),
            RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            Status = ApprovalStatus.Pending
        };
        Assert.True(MovementWorkflowRules.CanCancelTransfer(t));
        MovementWorkflowRules.SoftCancelTransfer(t, "اختبار");
        Assert.True(t.IsCancelled);
        Assert.False(MovementWorkflowRules.CanCancelTransfer(t));
        Assert.False(TransferWorkflowRules.CanExecute(t));
    }

    [Fact]
    public void Future_effective_date_blocks_execute()
    {
        var t = new TransferRequest
        {
            EmployeeId = Guid.NewGuid(),
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = Guid.NewGuid(),
            RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = ApprovalStatus.Approved
        };
        Assert.False(MovementWorkflowRules.IsEffectiveDateReached(t.EffectiveDate));
        Assert.False(TransferWorkflowRules.CanExecute(t));
    }

    [Fact]
    public void PreExecute_requires_recorded_and_approved_evidence()
    {
        var id = Guid.NewGuid();
        var items = MovementChecklistDefaults.CreateFor(MovementKind.Transfer, id).ToList();
        Assert.False(MovementWorkflowRules.PreExecuteChecklistComplete(items));
        MovementWorkflowRules.MarkStep(items.First(i => i.Step == MovementChecklistStep.RequestRecorded), Guid.NewGuid(), "e1");
        Assert.False(MovementWorkflowRules.PreExecuteChecklistComplete(items));
        MovementWorkflowRules.MarkStep(items.First(i => i.Step == MovementChecklistStep.Approved), Guid.NewGuid(), "e2");
        Assert.True(MovementWorkflowRules.PreExecuteChecklistComplete(items));
    }

    [Fact]
    public void Concurrent_movement_conflict_detected()
    {
        var emp = Guid.NewGuid();
        var active = new SecondmentRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp,
            HostOrg = "X",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            Status = SecondmentStatus.Active
        };
        var transfer = new TransferRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = emp,
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = Guid.NewGuid(),
            Status = ApprovalStatus.Approved,
            EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        Assert.True(MovementWorkflowRules.HasConcurrentMovementConflict(
            emp, transfer.Id, [transfer], [active], []));
    }

    [Fact]
    public void Sod_matrix_covers_secondment_and_loan()
    {
        var rules = SodMatrixDefaults.CreateDefaultRules();
        Assert.Contains(rules, r => r.EntityType == "SecondmentRequest");
        Assert.Contains(rules, r => r.EntityType == "LoanAssignmentRequest");
    }

    [Fact]
    public void Cancel_contracts_exist()
    {
        Assert.NotNull(typeof(ICancelTransferSaga).GetProperty(nameof(ICancelTransferSaga.Reason)));
        Assert.NotNull(typeof(ICancelSecondmentSaga).GetProperty(nameof(ICancelSecondmentSaga.Reason)));
        Assert.True(typeof(IExecuteTransferParty).GetProperty(nameof(IExecuteTransferParty.Compensate)) is not null);
        Assert.Equal("Auth.Directory", ProcessParties.AuthDirectory);
    }
}
