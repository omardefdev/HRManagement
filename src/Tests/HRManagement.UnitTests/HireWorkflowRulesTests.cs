using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;

namespace HRManagement.UnitTests;

public class HireWorkflowRulesTests
{
    [Fact]
    public void CanAdvanceToPendingApproval_RequiresContractAndCommencement_ForAppointment()
    {
        var txn = new ServiceTransaction
        {
            EmployeeId = Guid.NewGuid(),
            TransactionType = ServiceTransactionType.Appointment
        };

        Assert.False(HireWorkflowRules.CanAdvanceToPendingApproval(txn, hasContract: false, hasCommencement: true));
        Assert.False(HireWorkflowRules.CanAdvanceToPendingApproval(txn, hasContract: true, hasCommencement: false));
        Assert.True(HireWorkflowRules.CanAdvanceToPendingApproval(txn, hasContract: true, hasCommencement: true));
    }

    [Fact]
    public void CanAdvanceToPendingApproval_OtherTransactionTypes_AlwaysAllowed()
    {
        var txn = new ServiceTransaction
        {
            EmployeeId = Guid.NewGuid(),
            TransactionType = ServiceTransactionType.LeaveAdvance
        };

        Assert.True(HireWorkflowRules.CanAdvanceToPendingApproval(txn, hasContract: false, hasCommencement: false));
    }

    [Fact]
    public void CanPublishHireCompleted_RequiresAllChecklistSteps()
    {
        var txn = new ServiceTransaction
        {
            EmployeeId = Guid.NewGuid(),
            TransactionType = ServiceTransactionType.Appointment
        };
        txn.MoveTo(ServiceTransactionState.Submitted, null, null);
        txn.MoveTo(ServiceTransactionState.UnderReview, null, null);
        txn.MoveTo(ServiceTransactionState.InProcess, null, null);
        txn.MoveTo(ServiceTransactionState.PendingApproval, null, null);
        txn.MoveTo(ServiceTransactionState.Approved, null, null);
        txn.MoveTo(ServiceTransactionState.Executing, null, null);

        var incomplete = HireChecklistDefaults.CreateForTransaction(Guid.NewGuid()).ToList();
        incomplete[0].IsCompleted = true;
        Assert.False(HireWorkflowRules.CanPublishHireCompleted(txn, incomplete));

        foreach (var item in incomplete)
            item.IsCompleted = true;
        Assert.True(HireWorkflowRules.CanPublishHireCompleted(txn, incomplete));
    }
}
