using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;

namespace HRManagement.UnitTests;

public class ContractWorkflowRulesTests
{
    [Fact]
    public void ValidateCanActivate_Fails_When_Not_Pending()
    {
        var contract = new Contract
        {
            Status = ContractStatus.Active,
            Signatures = { new ContractSignature() }
        };

        var errors = ContractWorkflowRules.ValidateCanActivate(contract, hasCommencement: true, hasPassedProbationWhenRequired: true);
        Assert.Contains(errors, e => e.Contains("قيد الانتظار"));
    }

    [Fact]
    public void ValidateCanActivate_Fails_Without_Signature_Or_Commencement()
    {
        var contract = new Contract { Status = ContractStatus.Pending };

        var errors = ContractWorkflowRules.ValidateCanActivate(contract, hasCommencement: false, hasPassedProbationWhenRequired: true);
        Assert.True(errors.Count >= 2);
    }

    [Fact]
    public void Terminate_Sets_Status_Terminated()
    {
        var contract = new Contract { Status = ContractStatus.Active };
        ContractWorkflowRules.Terminate(contract);
        Assert.Equal(ContractStatus.Terminated, contract.Status);
    }
}
