using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Enums;
using Payroll.Domain.Entities;

namespace HRManagement.IntegrationTests;

public static class TestDataFactory
{
    public static ContractType CreateContractType(string code = "FT") =>
        new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            NameAr = "عقد دائم",
            NameEn = "Full Time"
        };

    public static Contract CreatePendingContract(Guid employeeId, ContractType contractType, int probationMonths = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            ContractTypeId = contractType.Id,
            ContractType = contractType,
            StartDate = new DateOnly(2026, 1, 1),
            Status = ContractStatus.Pending,
            ProbationMonths = probationMonths,
            Signatures = { new ContractSignature { SignedBy = Guid.NewGuid(), SignedAt = DateTime.UtcNow } }
        };

    public static CommencementRecord CreateCommencement(Guid employeeId, Guid contractId) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            ContractId = contractId,
            StartDate = new DateOnly(2026, 1, 1)
        };

    public static ProbationRecord CreatePassedProbation(Guid contractId) =>
        new()
        {
            Id = Guid.NewGuid(),
            ContractId = contractId,
            Result = ProbationResult.Passed,
            EndDate = new DateOnly(2026, 3, 1)
        };

    public static ServiceTransaction CreateLeaveTransaction(Guid employeeId) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            TransactionType = ServiceTransactionType.LeaveAdvance,
            Notes = "leave tx"
        };

    public static LeaveRequest CreatePendingLeave(Guid employeeId, Guid? serviceTransactionId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveType = LeaveType.Annual,
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 5),
            DurationDays = 5,
            Status = ApprovalStatus.Pending,
            ServiceTransactionId = serviceTransactionId
        };

    public static LeaveBalance CreateLeaveBalance(Guid employeeId, LeaveType leaveType, int year, int totalEntitlement, int used = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            LeaveType = leaveType,
            Year = year,
            TotalEntitlement = totalEntitlement,
            Used = used
        };

    public static AdvanceRequest CreatePendingAdvance(Guid employeeId, decimal amount, Guid? serviceTransactionId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            Amount = amount,
            Status = ApprovalStatus.Pending,
            ServiceTransactionId = serviceTransactionId
        };

    public static SeparationRecord CreateSeparationRecord(Guid employeeId, Guid? contractId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            ContractId = contractId,
            SeparationType = SeparationType.Resignation,
            EffectiveDate = new DateOnly(2026, 6, 30),
            SeparationEventPublished = false
        };

    public static EmployeePayProfile CreateActivePayProfile(Guid employeeId, decimal baseSalary = 10000m) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            BaseSalary = baseSalary,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1),
            IsActive = true
        };
}
