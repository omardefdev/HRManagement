using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Application.Models;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Contracts.Application.Features.CommencementRecords;

public record GetCommencementRecordsQuery : IRequest<IReadOnlyList<CommencementRecord>>;
public record GetCommencementRecordByIdQuery(Guid Id) : IRequest<CommencementRecord?>;
public record CreateCommencementRecordCommand(CommencementRecordDto Model) : IRequest<Guid>;
public record UpdateCommencementRecordCommand(Guid Id, CommencementRecordDto Model) : IRequest<bool>;
public record DeleteCommencementRecordCommand(Guid Id) : IRequest<bool>;

public class GetCommencementRecordsHandler(IContractsRepository repository) : IRequestHandler<GetCommencementRecordsQuery, IReadOnlyList<CommencementRecord>>
{
    public async Task<IReadOnlyList<CommencementRecord>> Handle(GetCommencementRecordsQuery request, CancellationToken cancellationToken) =>
        await repository.CommencementRecords
            .Include(c => c.Contract)
            .AsNoTracking()
            .OrderByDescending(x => x.StartDate)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync(cancellationToken);
}

public class GetCommencementRecordByIdHandler(IContractsRepository repository) : IRequestHandler<GetCommencementRecordByIdQuery, CommencementRecord?>
{
    public Task<CommencementRecord?> Handle(GetCommencementRecordByIdQuery request, CancellationToken cancellationToken) =>
        repository.GetCommencementRecordAsync(request.Id, cancellationToken);
}

public class CreateCommencementRecordHandler(IContractsRepository repository, IEventPublisher eventPublisher) : IRequestHandler<CreateCommencementRecordCommand, Guid>
{
    public async Task<Guid> Handle(CreateCommencementRecordCommand request, CancellationToken cancellationToken)
    {
        var vm = request.Model;
        var entity = new CommencementRecord
        {
            EmployeeId = vm.EmployeeId,
            ContractId = vm.ContractId,
            StartDate = vm.StartDate,
            NotifiedBy = vm.NotifiedBy,
            NotificationDate = vm.NotificationDate
        };
        await repository.AddAsync(entity, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        await HireCaseSupport.MarkContractClassifiedAsync(repository, vm.EmployeeId, "إثبات مباشرة", cancellationToken);

        var transaction = await HireCaseSupport.GetOpenAppointmentTransactionAsync(repository, vm.EmployeeId, cancellationToken);
        if (transaction is not null)
            await eventPublisher.PublishCommencementRecordedAsync(transaction.Id, vm.EmployeeId, vm.StartDate, correlationId: null, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }
}

public class UpdateCommencementRecordHandler(IContractsRepository repository) : IRequestHandler<UpdateCommencementRecordCommand, bool>
{
    public async Task<bool> Handle(UpdateCommencementRecordCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetCommencementRecordAsync(request.Id, cancellationToken);
        if (entity is null) return false;
        var vm = request.Model;
        entity.EmployeeId = vm.EmployeeId;
        entity.ContractId = vm.ContractId;
        entity.StartDate = vm.StartDate;
        entity.NotifiedBy = vm.NotifiedBy;
        entity.NotificationDate = vm.NotificationDate;
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class DeleteCommencementRecordHandler(IContractsRepository repository) : IRequestHandler<DeleteCommencementRecordCommand, bool>
{
    public async Task<bool> Handle(DeleteCommencementRecordCommand request, CancellationToken cancellationToken)
    {
        var entity = await repository.GetCommencementRecordAsync(request.Id, cancellationToken);
        if (entity is null) return false;
        repository.Remove(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}
