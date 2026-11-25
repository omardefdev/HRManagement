using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;
using Payroll.Domain.Services;

namespace Payroll.Application.Services;

/// <summary>
/// PLAN-56 (G-PAY-03): a run's lines with their components and the money rounding of its pinned framework version, as the
/// validation and the posting's check read them. One loader, so that both check the same thing.
/// </summary>
public sealed record PayrollRunLines(
    IReadOnlyList<SalaryLine> Lines,
    IReadOnlyDictionary<Guid, List<SalaryLineComponent>> ComponentsByLine,
    Func<decimal, decimal>? Round)
{
    public decimal SumNet => PayrollRunReconciliation.SumNet(Lines);

    public static async Task<PayrollRunLines> LoadAsync(
        IPayrollRepository repository,
        PayrollRun run,
        CancellationToken cancellationToken)
    {
        // Read from the store, not from the tracked collection: the check is of what is saved.
        var lines = await repository.SalaryLines
            .AsNoTracking()
            .Where(l => l.PayrollRunId == run.Id)
            .ToListAsync(cancellationToken);
        var components = await repository.SalaryLineComponents
            .AsNoTracking()
            .Where(c => repository.SalaryLines.Where(l => l.PayrollRunId == run.Id).Select(l => l.Id).Contains(c.SalaryLineId))
            .ToListAsync(cancellationToken);
        var byLine = components.GroupBy(c => c.SalaryLineId).ToDictionary(g => g.Key, g => g.ToList());

        Func<decimal, decimal>? round = null;
        if (run.PayrollConfigurationId is Guid frameworkId
            && await repository.GetConfigurationByIdAsync(frameworkId, cancellationToken) is { } framework)
            round = framework.RoundMoney;

        return new PayrollRunLines(lines, byLine, round);
    }
}
