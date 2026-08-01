using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Domain.Services;

public static class PendingContractProvisioning
{
    public static Contract CreatePending(Guid employeeId, Guid contractTypeId, DateOnly startDate, string terms) =>
        new()
        {
            EmployeeId = employeeId,
            ContractTypeId = contractTypeId,
            StartDate = startDate,
            Status = ContractStatus.Pending,
            Terms = terms
        };
}
