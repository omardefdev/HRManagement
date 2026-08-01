using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using HRManagement.SharedKernel.ViewModels;

namespace Payroll.Application.Features.EmployeePayProfiles;

public record UpsertEmployeePayProfileCommand(
    Guid EmployeeId,
    decimal BaseSalary,
    Guid? PayGradeId,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive,
    string? ChangeReason) : IRequest<(bool Success, Guid? ProfileId, string? Error)>;

public class UpsertEmployeePayProfileHandler(IPayrollRepository repository)
    : IRequestHandler<UpsertEmployeePayProfileCommand, (bool Success, Guid? ProfileId, string? Error)>
{
    public async Task<(bool Success, Guid? ProfileId, string? Error)> Handle(
        UpsertEmployeePayProfileCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetActiveProfileAsync(request.EmployeeId, cancellationToken);

        if (existing is not null && request.IsActive)
        {
            await repository.AddAsync(
                EmployeePayProfileRules.CreateHistorySnapshot(existing, request.ChangeReason ?? "تحديث ملف الراتب"),
                cancellationToken);
            existing.IsActive = false;
            existing.EffectiveTo = request.EffectiveFrom;
        }

        var profile = new EmployeePayProfile
        {
            EmployeeId = request.EmployeeId,
            BaseSalary = request.BaseSalary,
            PayGradeId = request.PayGradeId,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            IsActive = request.IsActive
        };

        await repository.AddAsync(profile, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return (true, profile.Id, null);
    }
}

public record GetEmployeePayProfilesQuery(bool ActiveOnly = false) : IRequest<IReadOnlyList<EmployeePayProfile>>;

public class GetEmployeePayProfilesHandler(IPayrollRepository repository)
    : IRequestHandler<GetEmployeePayProfilesQuery, IReadOnlyList<EmployeePayProfile>>
{
    public async Task<IReadOnlyList<EmployeePayProfile>> Handle(
        GetEmployeePayProfilesQuery request,
        CancellationToken cancellationToken)
    {
        var q = repository.EmployeePayProfiles.Include(p => p.PayGrade).AsNoTracking();
        if (request.ActiveOnly) q = q.Where(p => p.IsActive);
        return await q.OrderByDescending(p => p.EffectiveFrom).Take(SearchFilterBase.MaxPageSize).ToListAsync(cancellationToken);
    }
}

public record GetEmployeePayProfileHistoryQuery(Guid EmployeeId) : IRequest<IReadOnlyList<EmployeePayProfileHistory>>;

public class GetEmployeePayProfileHistoryHandler(IPayrollRepository repository)
    : IRequestHandler<GetEmployeePayProfileHistoryQuery, IReadOnlyList<EmployeePayProfileHistory>>
{
    public async Task<IReadOnlyList<EmployeePayProfileHistory>> Handle(
        GetEmployeePayProfileHistoryQuery request,
        CancellationToken cancellationToken) =>
        await repository.EmployeePayProfileHistories
            .AsNoTracking()
            .Where(h => h.EmployeeId == request.EmployeeId)
            .OrderByDescending(h => h.EffectiveFrom)
            .ToListAsync(cancellationToken);
}
