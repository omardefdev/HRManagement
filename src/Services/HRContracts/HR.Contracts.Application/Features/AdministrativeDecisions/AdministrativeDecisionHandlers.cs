using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Models;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Application.Services;
using HRManagement.ServiceDefaults.CodeGeneration;
using MediatR;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Contracts.Application.Features.AdministrativeDecisions;

public record GetAdministrativeDecisionsQuery : IRequest<IReadOnlyList<AdministrativeDecision>>;
public record GetAdministrativeDecisionByIdQuery(Guid Id) : IRequest<AdministrativeDecision?>;
public record CreateAdministrativeDecisionCommand(AdministrativeDecisionDto Model) : IRequest<Guid>;
public record UpdateAdministrativeDecisionCommand(Guid Id, AdministrativeDecisionDto Model) : IRequest<bool>;
public record DeleteAdministrativeDecisionCommand(Guid Id) : IRequest<bool>;

public class GetAdministrativeDecisionsHandler(IContractsRepository repository) : IRequestHandler<GetAdministrativeDecisionsQuery, IReadOnlyList<AdministrativeDecision>>
{
    public async Task<IReadOnlyList<AdministrativeDecision>> Handle(GetAdministrativeDecisionsQuery request, CancellationToken cancellationToken) =>
        await repository.AdministrativeDecisions.AsNoTracking().OrderByDescending(x => x.IssuedDate).Take(SearchFilterBase.MaxPageSize).ToListAsync(cancellationToken);
}

public class GetAdministrativeDecisionByIdHandler(IContractsRepository repository) : IRequestHandler<GetAdministrativeDecisionByIdQuery, AdministrativeDecision?>
{
    public Task<AdministrativeDecision?> Handle(GetAdministrativeDecisionByIdQuery request, CancellationToken cancellationToken) =>
        repository.GetAdministrativeDecisionAsync(request.Id, cancellationToken);
}

public class CreateAdministrativeDecisionHandler(
    IContractsRepository repository,
    IEventPublisher eventPublisher,
    ICodeGenerationClient codeClient) : IRequestHandler<CreateAdministrativeDecisionCommand, Guid>
{
    public async Task<Guid> Handle(CreateAdministrativeDecisionCommand request, CancellationToken cancellationToken)
    {
        var vm = request.Model;
        var entity = new AdministrativeDecision
        {
            Subject = vm.Subject.Trim(),
            DecisionNumber = vm.DecisionNumber.Trim(),
            IssuedBy = string.IsNullOrWhiteSpace(vm.IssuedBy) ? null : vm.IssuedBy.Trim(),
            IssuedDate = vm.IssuedDate,
            RelatedEmployeeId = vm.RelatedEmployeeId,
            FileId = vm.FileId
        };
        await repository.AddAsync(entity, cancellationToken);
        ServiceTransaction? transaction = null;
        if (vm.RelatedEmployeeId is Guid employeeId)
        {
            transaction = new ServiceTransaction
            {
                EmployeeId = employeeId,
                TransactionType = ServiceTransactionType.JobUpdate,
                Notes = "تسجيل قرار إداري"
            };
            await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, cancellationToken);
            await repository.AddAsync(transaction, cancellationToken);
        }
        await repository.SaveChangesAsync(cancellationToken);
        if (transaction is not null && vm.RelatedEmployeeId is Guid relatedEmployeeId)
        {
            await eventPublisher.PublishJobUpdateAppliedAsync(
                transaction.Id,
                entity.Id,
                relatedEmployeeId,
                entity.DecisionNumber,
                entity.Subject,
                correlationId: null,
                cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
        }
        return entity.Id;
    }
}

public class UpdateAdministrativeDecisionHandler(IContractsRepository repository) : IRequestHandler<UpdateAdministrativeDecisionCommand, bool>
{
    public async Task<bool> Handle(UpdateAdministrativeDecisionCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetAdministrativeDecisionAsync(request.Id, cancellationToken);
        if (entity is null) return false;
        var vm = request.Model;
        entity.Subject = vm.Subject.Trim();
        entity.DecisionNumber = vm.DecisionNumber.Trim();
        entity.IssuedDate = vm.IssuedDate;
        entity.RelatedEmployeeId = vm.RelatedEmployeeId;
        entity.FileId = vm.FileId;
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class DeleteAdministrativeDecisionHandler(IContractsRepository repository) : IRequestHandler<DeleteAdministrativeDecisionCommand, bool>
{
    public async Task<bool> Handle(DeleteAdministrativeDecisionCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetAdministrativeDecisionAsync(request.Id, cancellationToken);
        if (entity is null) return false;
        repository.Remove(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}
