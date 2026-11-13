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
