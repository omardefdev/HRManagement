using HR.Contracts.Domain.Entities;

namespace HR.Contracts.Application.Abstractions;

public interface IContractsRepository
{
    IQueryable<LeaveRequest> LeaveRequests { get; }
    IQueryable<AdvanceRequest> AdvanceRequests { get; }
    IQueryable<AdministrativeDecision> AdministrativeDecisions { get; }
    IQueryable<CommencementRecord> CommencementRecords { get; }
    IQueryable<ProbationRecord> ProbationRecords { get; }
    IQueryable<SeparationRecord> SeparationRecords { get; }
    IQueryable<ContractType> ContractTypes { get; }
    IQueryable<ContractTypeRegimeMap> ContractTypeRegimeMaps { get; }
    IQueryable<Contract> Contracts { get; }
    IQueryable<ServiceTransaction> ServiceTransactions { get; }
    IQueryable<LeaveBalance> LeaveBalances { get; }
    IQueryable<LeavePolicy> LeavePolicies { get; }
    IQueryable<AdvanceInstallmentPlan> AdvanceInstallmentPlans { get; }
    IQueryable<SeparationChecklist> SeparationChecklists { get; }
    IQueryable<HireChecklist> HireChecklists { get; }

    Task<LeaveRequest?> GetLeaveRequestAsync(Guid id, CancellationToken ct);
    Task<AdvanceRequest?> GetAdvanceRequestAsync(Guid id, CancellationToken ct);
    Task<AdministrativeDecision?> GetAdministrativeDecisionAsync(Guid id, CancellationToken ct);
    Task<CommencementRecord?> GetCommencementRecordAsync(Guid id, CancellationToken ct);
    Task<SeparationRecord?> GetSeparationRecordAsync(Guid id, CancellationToken ct);
    Task<ServiceTransaction?> GetServiceTransactionAsync(Guid id, CancellationToken ct);
    Task<Contract?> GetContractAsync(Guid id, CancellationToken ct);
    Task AddAsync<T>(T entity, CancellationToken ct) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
}
