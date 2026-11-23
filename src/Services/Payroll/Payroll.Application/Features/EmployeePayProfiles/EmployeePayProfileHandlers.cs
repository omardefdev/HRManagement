using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using HRManagement.SharedKernel.ViewModels;

namespace Payroll.Application.Features.EmployeePayProfiles;

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
