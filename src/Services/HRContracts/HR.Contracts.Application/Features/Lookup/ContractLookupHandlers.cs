using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Application.Features.Lookup;

/// <param name="SelectedContractId">The contract the form already holds.</param>
/// <param name="ContractIdsInScope">PLAN-52 (review 3, P-4): for a restricted caller, the ids of the contracts of his scope (a query of the same
/// context, so it composes into one statement); null for an unrestricted caller.</param>
public record GetContractsLookupQuery(Guid? SelectedContractId = null, IQueryable<Guid>? ContractIdsInScope = null)
    : IRequest<IReadOnlyList<ContractLookupItem>>;

public class GetContractsLookupHandler(IContractsRepository repository) : IRequestHandler<GetContractsLookupQuery, IReadOnlyList<ContractLookupItem>>
{
    public async Task<IReadOnlyList<ContractLookupItem>> Handle(GetContractsLookupQuery request, CancellationToken cancellationToken)
    {
        var contracts = repository.Contracts.AsNoTracking();
        if (request.ContractIdsInScope is { } inScope)
            contracts = contracts.Where(c => inScope.Contains(c.Id));
        return await contracts
            .Include(c => c.ContractType)
            .OrderByDescending(c => c.CreatedAt)
            .Take(500)
            .Select(c => new ContractLookupItem(c.Id, c.ContractType.NameAr + " — " + c.EmployeeId))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// PLAN-60 (§6, G-SEP-11): the separation forms list the chosen employee's own <c>Active</c> and <c>Pending</c> contracts, inside
/// PLAN-52's scope — never the newest 500 contracts of everyone. No employee chosen, no contract offered.
/// </summary>
public record GetEmployeeContractsLookupQuery(Guid EmployeeId, IQueryable<Guid>? ContractIdsInScope = null)
    : IRequest<IReadOnlyList<ContractLookupItem>>;

public class GetEmployeeContractsLookupHandler(IContractsRepository repository)
    : IRequestHandler<GetEmployeeContractsLookupQuery, IReadOnlyList<ContractLookupItem>>
{
    public async Task<IReadOnlyList<ContractLookupItem>> Handle(GetEmployeeContractsLookupQuery request, CancellationToken cancellationToken)
    {
        if (request.EmployeeId == Guid.Empty)
            return [];

        var contracts = repository.Contracts.AsNoTracking()
            .Where(c => c.EmployeeId == request.EmployeeId
                        && (c.Status == HR.Contracts.Domain.Enums.ContractStatus.Active
                            || c.Status == HR.Contracts.Domain.Enums.ContractStatus.Pending));
        if (request.ContractIdsInScope is { } inScope)
            contracts = contracts.Where(c => inScope.Contains(c.Id));
        return await contracts
            .Include(c => c.ContractType)
            .OrderByDescending(c => c.StartDate)
            .Select(c => new ContractLookupItem(
                c.Id,
                c.ContractType.NameAr + " — " + (c.Status == HR.Contracts.Domain.Enums.ContractStatus.Active ? "نشط" : "قيد الانتظار")
                + " — من " + c.StartDate.ToString()))
            .ToListAsync(cancellationToken);
    }
}
