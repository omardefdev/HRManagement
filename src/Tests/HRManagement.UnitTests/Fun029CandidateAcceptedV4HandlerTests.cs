using HR.Contracts.Application.Features.InboundEvents;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Fun029TestSupport;

namespace HRManagement.UnitTests;

/// <summary>FUN-029 (E): ICandidateAccepted v4 terms and lineage land on the contract and the transaction.</summary>
public class Fun029CandidateAcceptedV4HandlerTests
{
    [Fact]
    public async Task V4_terms_are_stored_on_the_pending_contract()
    {
        await using var db = CreateDb();
        await SeedContractTypeAsync(db, "DEFAULT");
        var employeeId = Guid.NewGuid();
        var command = V4Command(employeeId);

        await CreateHandler(db).Handle(command, CancellationToken.None);

        var contract = await db.Contracts.SingleAsync(c => c.EmployeeId == employeeId);
        Assert.Equal(ContractStatus.Pending, contract.Status);
        Assert.Equal("G5", contract.GradeCode);
        Assert.Equal(5000m, contract.AgreedBaseSalary);
        Assert.Equal("law50", contract.LegalRegimeId);
        Assert.Equal(HireEmploymentTypes.Permanent, contract.EmploymentType);
        Assert.Equal(command.OfferLetterId, contract.SourceOfferLetterId);
        Assert.Equal(new DateOnly(2026, 12, 1), contract.StartDate);
    }

    [Fact]
    public async Task Lineage_columns_are_set_on_the_appointment_transaction()
    {
        await using var db = CreateDb();
        await SeedContractTypeAsync(db, "DEFAULT");
        var employeeId = Guid.NewGuid();
        var command = V4Command(employeeId);

        await CreateHandler(db).Handle(command, CancellationToken.None);

        var txn = await db.ServiceTransactions.SingleAsync(t => t.EmployeeId == employeeId);
        Assert.Equal(ServiceTransactionType.Appointment, txn.TransactionType);
        Assert.Equal(command.RecruitmentApplicationId, txn.RecruitmentApplicationId);
        Assert.Equal(command.RequisitionId, txn.RequisitionId);
        Assert.Equal(command.OfferLetterId, txn.OfferLetterId);
        Assert.Equal(command.ExpectedStartDate, txn.ExpectedStartDate);
        // Notes are kept for readability.
        Assert.Contains($"recruitmentApplicationId:{command.RecruitmentApplicationId:D}", txn.Notes);
    }

    [Fact]
    public async Task V3_message_still_creates_the_appointment_and_contract()
    {
        await using var db = CreateDb();
        await SeedContractTypeAsync(db, "DEFAULT");
        var employeeId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        await CreateHandler(db).Handle(
            new ProcessCandidateAcceptedCommand(employeeId, "PERMANENT", new DateOnly(2026, 12, 1), "مرشح v3", applicationId),
            CancellationToken.None);

        var contract = await db.Contracts.SingleAsync(c => c.EmployeeId == employeeId);
        Assert.Null(contract.AgreedBaseSalary);
        Assert.Null(contract.GradeCode);
        Assert.Null(contract.LegalRegimeId);
        Assert.Equal(HireEmploymentTypes.Permanent, contract.EmploymentType);
        var txn = await db.ServiceTransactions.SingleAsync(t => t.EmployeeId == employeeId);
        Assert.Equal(applicationId, txn.RecruitmentApplicationId);
        Assert.True(await db.HireChecklists.AnyAsync(c => c.ServiceTransactionId == txn.Id));
    }

    [Fact]
    public async Task Internal_mobility_message_is_ignored()
    {
        await using var db = CreateDb();
        await SeedContractTypeAsync(db, "DEFAULT");
        var employeeId = Guid.NewGuid();

        await CreateHandler(db).Handle(V4Command(employeeId, internalMobility: true), CancellationToken.None);

        Assert.False(await db.ServiceTransactions.AnyAsync(t => t.EmployeeId == employeeId));
        Assert.False(await db.Contracts.AnyAsync(c => c.EmployeeId == employeeId));
    }

    [Fact]
    public async Task Redelivery_creates_no_duplicate_transaction_contract_or_checklist()
    {
        await using var db = CreateDb();
        await SeedContractTypeAsync(db, "DEFAULT");
        var employeeId = Guid.NewGuid();
        var command = V4Command(employeeId);

        await CreateHandler(db).Handle(command, CancellationToken.None);
        var checklistCount = await db.HireChecklists.CountAsync();
        await CreateHandler(db).Handle(command, CancellationToken.None);

        Assert.Equal(1, await db.ServiceTransactions.CountAsync(t => t.EmployeeId == employeeId));
        Assert.Equal(1, await db.Contracts.CountAsync(c => c.EmployeeId == employeeId));
        Assert.Equal(checklistCount, await db.HireChecklists.CountAsync());
        var txn = await db.ServiceTransactions.SingleAsync(t => t.EmployeeId == employeeId);
        Assert.Equal(command.RecruitmentApplicationId, txn.RecruitmentApplicationId);
    }

    [Theory]
    [InlineData("Permanent", null, "Permanent")]
    [InlineData(null, "PERMANENT", "Permanent")]
    [InlineData(null, "CONTRACT", "Temporary")]
    [InlineData("administrative", "PERMANENT", "Administrative")]
    [InlineData(null, "INTERNSHIP", null)]
    public void Employment_type_is_normalized_from_v4_or_the_legacy_category(string? type, string? category, string? expected) =>
        Assert.Equal(expected, HireEmploymentTypes.Normalize(type, category));
}
