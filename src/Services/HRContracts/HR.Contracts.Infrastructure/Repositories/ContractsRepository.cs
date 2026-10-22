using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Infrastructure.Repositories;

public class ContractsRepository(ContractsDbContext db) : IContractsRepository
{
    public IQueryable<LeaveRequest> LeaveRequests => db.LeaveRequests;
    public IQueryable<AdvanceRequest> AdvanceRequests => db.AdvanceRequests;
    public IQueryable<AdministrativeDecision> AdministrativeDecisions => db.AdministrativeDecisions;
    public IQueryable<CommencementRecord> CommencementRecords => db.CommencementRecords;
    public IQueryable<ProbationRecord> ProbationRecords => db.ProbationRecords;
    public IQueryable<SeparationRecord> SeparationRecords => db.SeparationRecords;
    public IQueryable<ContractType> ContractTypes => db.ContractTypes;
    public IQueryable<ContractTypeRegimeMap> ContractTypeRegimeMaps => db.ContractTypeRegimeMaps;
    public IQueryable<Contract> Contracts => db.Contracts;
    public IQueryable<ServiceTransaction> ServiceTransactions => db.ServiceTransactions;
    public IQueryable<LeaveBalance> LeaveBalances => db.LeaveBalances;
    public IQueryable<LeavePolicy> LeavePolicies => db.LeavePolicies;
    public IQueryable<AdvanceInstallmentPlan> AdvanceInstallmentPlans => db.AdvanceInstallmentPlans;
    public IQueryable<SeparationChecklist> SeparationChecklists => db.SeparationChecklists;
    public IQueryable<HireChecklist> HireChecklists => db.HireChecklists;

    public Task<LeaveRequest?> GetLeaveRequestAsync(Guid id, CancellationToken ct) =>
        db.LeaveRequests.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<AdvanceRequest?> GetAdvanceRequestAsync(Guid id, CancellationToken ct) =>
        db.AdvanceRequests.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<AdministrativeDecision?> GetAdministrativeDecisionAsync(Guid id, CancellationToken ct) =>
        db.AdministrativeDecisions.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<CommencementRecord?> GetCommencementRecordAsync(Guid id, CancellationToken ct) =>
        db.CommencementRecords.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<SeparationRecord?> GetSeparationRecordAsync(Guid id, CancellationToken ct) =>
        db.SeparationRecords.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<ServiceTransaction?> GetServiceTransactionAsync(Guid id, CancellationToken ct) =>
        db.ServiceTransactions
            .Include(x => x.StateHistory)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<Contract?> GetContractAsync(Guid id, CancellationToken ct) =>
        db.Contracts.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task AddAsync<T>(T entity, CancellationToken ct) where T : class =>
        db.Set<T>().AddAsync(entity, ct).AsTask();

    public void Remove<T>(T entity) where T : class => db.Set<T>().Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
