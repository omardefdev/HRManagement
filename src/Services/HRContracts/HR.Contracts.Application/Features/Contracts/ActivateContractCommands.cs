using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Application.Features.Contracts;

public record ActivateContractCommand(Guid ContractId) : IRequest<(bool Success, string? Error)>;

public class ActivateContractHandler(IContractsRepository repository, IEventPublisher eventPublisher)
    : IRequestHandler<ActivateContractCommand, (bool Success, string? Error)>
{
    public async Task<(bool Success, string? Error)> Handle(ActivateContractCommand request, CancellationToken cancellationToken)
    {
        var contract = await repository.Contracts
            .Include(c => c.ContractType)
            .Include(c => c.Signatures)
            .FirstOrDefaultAsync(c => c.Id == request.ContractId, cancellationToken);

        if (contract is null)
            return (false, null);

        var hasCommencement = await repository.CommencementRecords
            .AnyAsync(c => c.ContractId == contract.Id, cancellationToken);

        var hasPassedProbation = !contract.ProbationMonths.HasValue || contract.ProbationMonths.Value <= 0
            || await repository.ProbationRecords.AnyAsync(
                p => p.ContractId == contract.Id && p.Result == ProbationResult.Passed, cancellationToken);

        var errors = ContractWorkflowRules.ValidateCanActivate(contract, hasCommencement, hasPassedProbation);
        if (errors.Count > 0)
            return (false, string.Join(" | ", errors));

        contract.Status = ContractStatus.Active;
        await repository.SaveChangesAsync(cancellationToken);

        await eventPublisher.PublishContractActivatedAsync(contract, cancellationToken);
        await HireCaseSupport.MarkPayrollLinkedAsync(repository, contract.EmployeeId, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
