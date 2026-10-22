using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Messaging;
using HR.Contracts.Infrastructure.Repositories;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using static HRManagement.UnitTests.Fun029TestSupport;

namespace HRManagement.UnitTests;

/// <summary>FUN-029 (F): contract type by service regime and employment type — no silent fallback.</summary>
public class Fun029ContractTypeResolverTests
{
    private static async Task<ContractType> MapAsync(ContractsDbContext db, string code, string? regime, string? type, bool active = true)
    {
        var contractType = await SeedContractTypeAsync(db, code);
        db.ContractTypeRegimeMaps.Add(new ContractTypeRegimeMap
        {
            LegalRegimeId = regime,
            EmploymentType = type,
            ContractTypeId = contractType.Id,
            IsActive = active
        });
        await db.SaveChangesAsync();
        return contractType;
    }

    private static ContractTypeResolver Resolver(ContractsDbContext db, bool fallback = true) =>
        new(new ContractsRepository(db), new HireContractOptions { AllowDefaultContractTypeFallback = fallback });

    [Theory]
    [InlineData("law50", "Permanent", "EXACT", ContractTypeMatch.RegimeAndType)]
    [InlineData("LAW50", "permanent", "EXACT", ContractTypeMatch.RegimeAndType)]
    [InlineData("law50", "Temporary", "REGIME", ContractTypeMatch.RegimeAnyType)]
    [InlineData("law53", "Permanent", "TYPE", ContractTypeMatch.AnyRegimeType)]
    [InlineData(null, "Permanent", "TYPE", ContractTypeMatch.AnyRegimeType)]
    [InlineData("law53", "Temporary", "ANY", ContractTypeMatch.AnyRegimeAnyType)]
    [InlineData(null, null, "ANY", ContractTypeMatch.AnyRegimeAnyType)]
    public async Task Most_specific_map_wins(string? regime, string? type, string expectedCode, ContractTypeMatch expectedMatch)
    {
        await using var db = CreateDb();
        await MapAsync(db, "ANY", null, null);
        await MapAsync(db, "TYPE", null, "Permanent");
        await MapAsync(db, "REGIME", "law50", null);
        await MapAsync(db, "EXACT", "law50", "Permanent");

        var resolution = await Resolver(db).ResolveAsync(regime, type);

        Assert.Equal(expectedCode, resolution.ContractType?.Code);
        Assert.Equal(expectedMatch, resolution.Match);
    }

    [Fact]
    public async Task Inactive_maps_are_ignored()
    {
        await using var db = CreateDb();
        await MapAsync(db, "OLD", "law50", "Permanent", active: false);
        await MapAsync(db, "REGIME", "law50", null);

        Assert.Equal("REGIME", (await Resolver(db).ResolveAsync("law50", "Permanent")).ContractType?.Code);
    }

    [Fact]
    public async Task Default_is_used_only_when_the_fallback_is_on()
    {
        await using var db = CreateDb();
        await SeedContractTypeAsync(db, ContractTypeResolver.DefaultContractTypeCode);
        await MapAsync(db, "LAW53", "law53", null);

        var on = await Resolver(db, fallback: true).ResolveAsync("law50", "Permanent");
        var off = await Resolver(db, fallback: false).ResolveAsync("law50", "Permanent");

        Assert.Equal(ContractTypeMatch.DefaultFallback, on.Match);
        Assert.Equal("DEFAULT", on.ContractType?.Code);
        Assert.Equal(ContractTypeMatch.Unresolved, off.Match);
        Assert.False(off.IsResolved);
    }

    [Fact]
    public async Task Hire_uses_the_mapped_contract_type()
    {
        await using var db = CreateDb();
        await SeedContractTypeAsync(db, "DEFAULT");
        var mapped = await MapAsync(db, "ACC-LAW50-PERM", "law50", "Permanent");
        var employeeId = Guid.NewGuid();

        await CreateHandler(db).Handle(V4Command(employeeId), CancellationToken.None);

        var contract = await db.Contracts.SingleAsync(c => c.EmployeeId == employeeId);
        Assert.Equal(mapped.Id, contract.ContractTypeId);
        Assert.Null((await db.ServiceTransactions.SingleAsync(t => t.EmployeeId == employeeId)).PendingContractTerms);
    }

    [Fact]
    public async Task Unresolved_hire_has_no_contract_a_checklist_note_and_an_intervention()
    {
        await using var db = CreateDb();
        await SeedContractTypeAsync(db, "DEFAULT");
        var publisher = new Mock<IEventPublisher>();
        var employeeId = Guid.NewGuid();

        await CreateHandler(db, publisher.Object, allowDefaultFallback: false)
            .Handle(V4Command(employeeId, employmentType: "Administrative"), CancellationToken.None);

        Assert.False(await db.Contracts.AnyAsync(c => c.EmployeeId == employeeId));
        var txn = await db.ServiceTransactions.SingleAsync(t => t.EmployeeId == employeeId);
        Assert.Equal(ServiceTransactionType.Appointment, txn.TransactionType);
        Assert.NotNull(txn.PendingContractTerms);
        var expected = HireContractMessages.UnresolvedContractType("law50", "Administrative");
        Assert.Equal("لا يوجد نوع عقد مطابق لنظام الخدمة law50 / Administrative — يتطلب تدخلاً", expected);
        var item = await db.HireChecklists.SingleAsync(c => c.ServiceTransactionId == txn.Id && c.Step == HireStep.ContractClassified);
        Assert.False(item.IsCompleted);
        Assert.Equal(expected, item.Notes);
        publisher.Verify(p => p.PublishProcessInterventionRequestedAsync(
            SagaTypeNames.Hire, txn.Id, expected, "HR.Contracts", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unresolved_redelivery_does_not_publish_a_second_intervention_or_create_a_contract()
    {
        await using var db = CreateDb();
        var publisher = new Mock<IEventPublisher>();
        var employeeId = Guid.NewGuid();
        var command = V4Command(employeeId, employmentType: "Administrative");

        await CreateHandler(db, publisher.Object, allowDefaultFallback: false).Handle(command, CancellationToken.None);
        await CreateHandler(db, publisher.Object, allowDefaultFallback: false).Handle(command, CancellationToken.None);

        Assert.Equal(1, await db.ServiceTransactions.CountAsync(t => t.EmployeeId == employeeId));
        Assert.False(await db.Contracts.AnyAsync());
        publisher.Verify(p => p.PublishProcessInterventionRequestedAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Manual_pending_contract_uses_the_kept_offer_terms_and_clears_the_banner_state()
    {
        await using var db = CreateDb();
        var chosen = await SeedContractTypeAsync(db, "MANUAL");
        var employeeId = Guid.NewGuid();
        await CreateHandler(db, allowDefaultFallback: false)
            .Handle(V4Command(employeeId, employmentType: "Administrative"), CancellationToken.None);
        var txn = await db.ServiceTransactions.SingleAsync(t => t.EmployeeId == employeeId);

        var (ok, error) = await new CreatePendingHireContractHandler(new ContractsRepository(db))
            .Handle(new CreatePendingHireContractCommand(txn.Id, chosen.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(ok, error);
        var contract = await db.Contracts.SingleAsync(c => c.EmployeeId == employeeId);
        Assert.Equal(chosen.Id, contract.ContractTypeId);
        Assert.Equal(ContractStatus.Pending, contract.Status);
        Assert.Equal(5000m, contract.AgreedBaseSalary);
        Assert.Equal("G5", contract.GradeCode);
        Assert.Equal("Administrative", contract.EmploymentType);
        Assert.Equal(new DateOnly(2026, 12, 1), contract.StartDate);
        Assert.Null((await db.ServiceTransactions.SingleAsync(t => t.Id == txn.Id)).PendingContractTerms);

        var (again, againError) = await new CreatePendingHireContractHandler(new ContractsRepository(db))
            .Handle(new CreatePendingHireContractCommand(txn.Id, chosen.Id, null), CancellationToken.None);
        Assert.False(again);
        Assert.Equal(CreatePendingHireContractHandler.AlreadyHasContractMessage, againError);
    }

    [Fact]
    public async Task MassTransit_publisher_sends_the_intervention_request()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness()
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var transactionId = Guid.NewGuid();
            var tenantId = Guid.NewGuid();
            var publisher = new MassTransitEventPublisher(harness.Bus, Mock.Of<IExecutionContextAccessor>());

            await publisher.PublishProcessInterventionRequestedAsync(
                SagaTypeNames.Hire, transactionId, "سبب", "HR.Contracts", tenantId, CancellationToken.None);

            Assert.True(await harness.Published.Any<IProcessInterventionRequested>(x =>
                x.Context.Message.CorrelationId == transactionId
                && x.Context.Message.SagaType == SagaTypeNames.Hire
                && x.Context.Message.FailedParty == "HR.Contracts"
                && x.Context.Message.TenantId == tenantId
                && x.Context.Message.Reason == "سبب"));
        }
        finally
        {
            await harness.Stop();
        }
    }
}
