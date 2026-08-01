using HRManagement.MessageContracts.Events;

namespace HRManagement.UnitTests;

public class MessageContractTests
{
    private sealed record ContractActivatedStub(
        Guid ContractId,
        Guid EmployeeId,
        string ContractType,
        DateTime StartDate,
        DateTime? EndDate,
        decimal? BaseSalary = null,
        string? PayGradeCode = null,
        string? CorrelationId = null,
        string? SourceSystem = null) : IContractActivated;

    [Fact]
    public void IContractActivated_contract_shape_is_usable()
    {
        IContractActivated msg = new ContractActivatedStub(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "PERM",
            DateTime.UtcNow,
            null);
        Assert.NotEqual(Guid.Empty, msg.ContractId);
        Assert.Equal(2, msg.SchemaVersion);
    }
}
