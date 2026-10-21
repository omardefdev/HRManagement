using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Models;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Application.Services;
using HRManagement.ServiceDefaults.CodeGeneration;
using MediatR;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Contracts.Application.Features.ProbationRecords;

public record GetProbationRecordsQuery : IRequest<IReadOnlyList<ProbationRecord>>;
public record GetProbationRecordByIdQuery(Guid Id) : IRequest<ProbationRecord?>;
public record CreateProbationRecordCommand(ProbationRecordDto Model) : IRequest<Guid>;
public record UpdateProbationRecordCommand(Guid Id, ProbationRecordDto Model) : IRequest<bool>;
public record DeleteProbationRecordCommand(Guid Id) : IRequest<bool>;

public class GetProbationRecordsHandler(IContractsRepository repository) : IRequestHandler<GetProbationRecordsQuery, IReadOnlyList<ProbationRecord>>
{
    public async Task<IReadOnlyList<ProbationRecord>> Handle(GetProbationRecordsQuery request, CancellationToken cancellationToken) =>
        await repository.ProbationRecords
            .Include(p => p.Contract)
            .ThenInclude(c => c.ContractType)
            .AsNoTracking()
            .OrderByDescending(x => x.StartDate)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync(cancellationToken);
}

public class GetProbationRecordByIdHandler(IContractsRepository repository) : IRequestHandler<GetProbationRecordByIdQuery, ProbationRecord?>
{
    public Task<ProbationRecord?> Handle(GetProbationRecordByIdQuery request, CancellationToken cancellationToken) =>
        repository.ProbationRecords.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
}

public class CreateProbationRecordHandler(
    IContractsRepository repository,
    IEventPublisher eventPublisher,
    ICodeGenerationClient codeClient) : IRequestHandler<CreateProbationRecordCommand, Guid>
{
    public async Task<Guid> Handle(CreateProbationRecordCommand request, CancellationToken cancellationToken)
    {
        var vm = request.Model;
        var entity = new ProbationRecord
        {
            ContractId = vm.ContractId,
            EmployeeId = vm.EmployeeId,
            StartDate = vm.StartDate,
            EndDate = vm.EndDate,
            Result = vm.Result,
            DecisionBy = vm.DecisionBy,
            DecisionDate = vm.DecisionDate,
            Notes = string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim()
        };
        await repository.AddAsync(entity, cancellationToken);
        var transaction = new ServiceTransaction
        {
            EmployeeId = vm.EmployeeId,
            TransactionType = ServiceTransactionType.Confirmation,
            Notes = "تسجيل نتيجة فترة التجربة"
        };
        await ServiceTransactionReferenceSupport.ApplyReferenceAsync(transaction, codeClient, cancellationToken);
        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await eventPublisher.PublishConfirmationApprovedAsync(
            transaction.Id,
            vm.EmployeeId,
            vm.ContractId,
            vm.Result.ToString(),
            correlationId: null,
            cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}

public class UpdateProbationRecordHandler(IContractsRepository repository) : IRequestHandler<UpdateProbationRecordCommand, bool>
{
    public async Task<bool> Handle(UpdateProbationRecordCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.ProbationRecords.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (entity is null) return false;
        var vm = request.Model;
        entity.ContractId = vm.ContractId;
        entity.EmployeeId = vm.EmployeeId;
        entity.StartDate = vm.StartDate;
        entity.EndDate = vm.EndDate;
        entity.Result = vm.Result;
        entity.DecisionBy = vm.DecisionBy;
        entity.DecisionDate = vm.DecisionDate;
        entity.Notes = string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim();
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class DeleteProbationRecordHandler(IContractsRepository repository) : IRequestHandler<DeleteProbationRecordCommand, bool>
{
    public async Task<bool> Handle(DeleteProbationRecordCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.ProbationRecords.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (entity is null) return false;
        repository.Remove(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}
