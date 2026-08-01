using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Application.Features.Lookup;

public record GetContractsLookupQuery(Guid? SelectedContractId = null) : IRequest<IReadOnlyList<ContractLookupItem>>;

public class GetContractsLookupHandler(IContractsRepository repository) : IRequestHandler<GetContractsLookupQuery, IReadOnlyList<ContractLookupItem>>
{
    public async Task<IReadOnlyList<ContractLookupItem>> Handle(GetContractsLookupQuery request, CancellationToken cancellationToken) =>
        await repository.Contracts
            .AsNoTracking()
            .Include(c => c.ContractType)
            .OrderByDescending(c => c.CreatedAt)
            .Take(500)
            .Select(c => new ContractLookupItem(c.Id, c.ContractType.NameAr + " — " + c.EmployeeId))
            .ToListAsync(cancellationToken);
}
