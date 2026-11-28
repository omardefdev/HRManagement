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
        var contract = new Contract { Status = ContractStatus.Active, StartDate = new DateOnly(2025, 1, 1) };
        ContractWorkflowRules.Terminate(contract, new DateOnly(2026, 11, 30));
        Assert.Equal(ContractStatus.Terminated, contract.Status);
        // PLAN-60: with the separation's effective date as its end.
        Assert.Equal(new DateOnly(2026, 11, 30), contract.EndDate);
    }

    [Fact]
    public void Terminate_Refuses_An_End_Before_The_Start()
    {
        var contract = new Contract { Status = ContractStatus.Active, StartDate = new DateOnly(2026, 6, 1) };
        Assert.Equal(ContractWorkflowRules.EndBeforeStartMessage,
            Assert.Throws<InvalidOperationException>(() => ContractWorkflowRules.Terminate(contract, new DateOnly(2026, 5, 31))).Message);
        Assert.Equal(ContractStatus.Active, contract.Status);
        Assert.Null(contract.EndDate);
    }
}
