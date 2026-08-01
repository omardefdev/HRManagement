using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;

namespace HRManagement.UnitTests;

public class ServiceTransactionWorkflowTests
{
    [Fact]
    public void MoveTo_Allows_Valid_Path_To_Archived()
    {
        var tx = new ServiceTransaction
        {
            EmployeeId = Guid.NewGuid(),
            TransactionType = ServiceTransactionType.Separation
        };

        tx.MoveTo(ServiceTransactionState.Submitted, null);
        tx.MoveTo(ServiceTransactionState.UnderReview, null);
        tx.MoveTo(ServiceTransactionState.InProcess, null);
        tx.MoveTo(ServiceTransactionState.PendingApproval, null);
        tx.MoveTo(ServiceTransactionState.Approved, null);
        tx.MoveTo(ServiceTransactionState.Executing, null);
        tx.MoveTo(ServiceTransactionState.PostProcessing, null);
        tx.MoveTo(ServiceTransactionState.Completed, null);
        tx.MoveTo(ServiceTransactionState.Archived, null);

        Assert.Equal(ServiceTransactionState.Archived, tx.State);
        Assert.Equal(9, tx.StateHistory.Count);
    }

    [Fact]
    public void MoveTo_Throws_On_Invalid_Transition()
    {
        var tx = new ServiceTransaction
        {
            EmployeeId = Guid.NewGuid(),
            TransactionType = ServiceTransactionType.JobUpdate
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            tx.MoveTo(ServiceTransactionState.Archived, null));

        Assert.Contains("Invalid state transition", ex.Message);
    }
}
