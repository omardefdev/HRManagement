using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Messaging;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>FUN-029 (G): IContractActivated carries the agreed salary and grade of the contract.</summary>
public class Fun029ContractActivatedTests
{
    private static async Task<IContractActivated> PublishAsync(Contract contract)
    {
        await using var provider = new ServiceCollection().AddMassTransitTestHarness().BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await new MassTransitEventPublisher(harness.Bus, Mock.Of<IExecutionContextAccessor>())
                .PublishContractActivatedAsync(contract, CancellationToken.None);
            Assert.True(await harness.Published.Any<IContractActivated>());
            return harness.Published.Select<IContractActivated>().First().Context.Message;
        }
        finally
        {
            await harness.Stop();
        }
    }

    private static Contract NewContract(decimal? salary, string? grade) => new()
    {
        EmployeeId = Guid.NewGuid(),
        ContractType = new ContractType { Code = "ACC-LAW50-PERM", NameAr = "تجريبي" },
        StartDate = new DateOnly(2026, 12, 1),
        Status = ContractStatus.Active,
        AgreedBaseSalary = salary,
        GradeCode = grade
    };

    [Fact]
    public async Task Salary_and_grade_are_published_from_the_contract()
    {
        var message = await PublishAsync(NewContract(5000m, "G5"));

        Assert.Equal(5000m, message.BaseSalary);
        Assert.Equal("G5", message.PayGradeCode);
        Assert.Equal("ACC-LAW50-PERM", message.ContractType);
    }

    [Fact]
    public async Task Manual_contract_without_terms_keeps_null()
    {
        var message = await PublishAsync(NewContract(null, null));

        Assert.Null(message.BaseSalary);
        Assert.Null(message.PayGradeCode);
    }
}
