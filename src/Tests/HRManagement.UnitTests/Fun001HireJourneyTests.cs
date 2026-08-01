using System.Text.Json;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;

namespace HRManagement.UnitTests;

public class Fun001JourneyMatrixTests
{
    private static string ResolveMatrixPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "fun-hire", "journey-matrix.json");
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("docs/fun-hire/journey-matrix.json");
    }

    [Fact]
    public void Matrix_includes_AuthDirectory_as_mandatory()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(ResolveMatrixPath()));
        var dir = doc.RootElement.GetProperty("steps").EnumerateArray()
            .First(s => s.GetProperty("id").GetString() == "DirectoryAccount");
        Assert.True(dir.GetProperty("mandatory").GetBoolean());
        Assert.Equal("Auth.Directory", dir.GetProperty("service").GetString());
    }

    [Fact]
    public void HireSagaOptions_defaults_include_AuthDirectory()
    {
        var opts = new HR.Contracts.Infrastructure.Sagas.HireSagaOptions();
        Assert.Contains(ProcessParties.AuthDirectory, opts.MandatoryParties);
    }
}

public class Fun001ChecklistEvidenceTests
{
    [Fact]
    public void MarkStepCompleted_requires_evidence_for_IdentityIssued()
    {
        var item = new HireChecklist { Step = HireStep.IdentityIssued };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            HireWorkflowRules.MarkStepCompleted(item));
        Assert.Contains("EvidenceRef", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkStepCompleted_sets_governance_fields()
    {
        var item = HireChecklistDefaults.CreateForTransaction(Guid.NewGuid())
            .First(c => c.Step == HireStep.IdentityIssued);
        Assert.Equal("idcard", item.OwnerRole);
        Assert.Equal(72, item.SlaHours);
        HireWorkflowRules.MarkStepCompleted(item, evidenceRef: "card:issued-1", completedBy: Guid.NewGuid());
        Assert.True(item.IsCompleted);
        Assert.Equal("card:issued-1", item.EvidenceRef);
    }

    [Fact]
    public void Duplicate_open_appointment_detected()
    {
        var employeeId = Guid.NewGuid();
        var open = new ServiceTransaction
        {
            EmployeeId = employeeId,
            TransactionType = ServiceTransactionType.Appointment
        };
        open.MoveTo(ServiceTransactionState.Submitted, null, null);
        open.MoveTo(ServiceTransactionState.UnderReview, null, null);
        open.MoveTo(ServiceTransactionState.InProcess, null, null);
        Assert.True(HireWorkflowRules.HasOpenAppointmentTransaction([open]));
    }

    [Fact]
    public void Cancelled_appointment_is_rejected_path()
    {
        var txn = new ServiceTransaction
        {
            EmployeeId = Guid.NewGuid(),
            TransactionType = ServiceTransactionType.Appointment
        };
        txn.MoveTo(ServiceTransactionState.Cancelled, null, "reject");
        Assert.True(HireWorkflowRules.IsRejectedOrCancelled(txn));
    }
}

public class Fun001HireCompensateDispatchTests
{
    [Fact]
    public void Cancel_command_contract_exists()
    {
        // Compile-time presence of FUN-001 cancel surface
        Assert.Equal("Auth.Directory", ProcessParties.AuthDirectory);
        Assert.True(typeof(ICancelHireSaga).IsInterface);
        Assert.True(typeof(IExecuteHireParty).GetProperty(nameof(IExecuteHireParty.Compensate)) is not null);
    }
}
