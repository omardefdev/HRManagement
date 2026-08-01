using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;

namespace HRManagement.UnitTests;

public class ServiceTransactionWorkflowHelperTests
{
    [Fact]
    public void AdvanceToCompleted_Reaches_Completed_From_Draft()
    {
        var tx = new ServiceTransaction
        {
            EmployeeId = Guid.NewGuid(),
            TransactionType = ServiceTransactionType.LeaveAdvance
        };

        ServiceTransactionWorkflowHelper.AdvanceToCompleted(tx, null, "test");
        Assert.Equal(ServiceTransactionState.Completed, tx.State);
    }
}
