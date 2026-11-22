using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Enums;

namespace Payroll.Application.Features.PayProfileChanges;

/// <summary>The outcome of one activation cycle in one agency.</summary>
/// <param name="Due">Scheduled requests whose effective date has come.</param>
/// <param name="Applied">Those applied in this cycle.</param>
/// <param name="Blocked">Those refused in this cycle (they stay scheduled, with their reason).</param>
/// <param name="BlockedTotal">Every scheduled request of the agency that carries a refusal reason now.</param>
public sealed record PayProfileChangeActivationSummary(int Due, int Applied, int Blocked, int BlockedTotal);

/// <summary>
/// PLAN-53 (P-7): applies, in the current agency, the scheduled requests that are due (their effective date is today or earlier, in the
/// configured zone), each through the applier with its own save. A refusal leaves the request scheduled with an Arabic reason for the
/// approver. The job and <c>POST api/payroll/pay-profile-changes/activate-due</c> run this same code. Idempotent: an applied request is
/// no longer scheduled, and the request's row version refuses a second application that raced the first.
/// </summary>
public sealed class PayProfileChangeActivator(
    IPayrollRepository repository,
    IPayProfileChangeApplier applier,
    IPayrollCalendar calendar,
    ILogger<PayProfileChangeActivator> logger)
{
    public async Task<PayProfileChangeActivationSummary> ActivateDueAsync(CancellationToken ct = default)
    {
        var today = calendar.Today;
        // Every request that is due when the cycle starts, each attempted once: the ids are read first (a refused request stays
        // scheduled, so reading page after page of «still scheduled» would meet it again and never reach the ones behind it).
        var due = await repository.PayProfileChangeRequests.AsNoTracking()
            .Where(r => r.State == PayProfileChangeState.Scheduled && r.EffectiveFrom <= today)
            .OrderBy(r => r.EffectiveFrom).ThenBy(r => r.RequestedAt).ThenBy(r => r.Id)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var (applied, blocked) = (0, 0);
        foreach (var id in due)
        {
            ct.ThrowIfCancellationRequested();
            // Each request is its own unit of work: nothing an earlier attempt left tracked reaches this save.
            repository.ClearChangeTracker();
            var request = await repository.PayProfileChangeRequests
                .FirstOrDefaultAsync(r => r.Id == id && r.State == PayProfileChangeState.Scheduled, ct);
            if (request is null)
                continue;

            PayProfileChangeResult result;
            try
            {
                result = await applier.ApplyAsync(request, approval: null, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // No amount, reason or legal reference in the log: the id only.
                logger.LogError(ex, "Applying the scheduled salary change {RequestId} failed.", id);
                result = PayProfileChangeResult.Fail(PayProfileChangeFailure.Conflict, PayProfileChangeMessages.ApplyFailed, id);
            }

            if (result.Success)
            {
                applied++;
                continue;
            }

            if (await RecordRefusalAsync(id, result.Error ?? PayProfileChangeMessages.ApplyFailed, ct))
                blocked++;
        }

        repository.ClearChangeTracker();
        var blockedTotal = await repository.PayProfileChangeRequests.AsNoTracking()
            .CountAsync(r => r.State == PayProfileChangeState.Scheduled && r.LastApplyError != null, ct);
        return new PayProfileChangeActivationSummary(due.Count, applied, blocked, blockedTotal);
    }

    /// <summary>Keeps the request scheduled and writes why it was not applied. False when it is no longer scheduled (another replica applied it).</summary>
    private async Task<bool> RecordRefusalAsync(Guid id, string error, CancellationToken ct)
    {
        repository.ClearChangeTracker();
        var request = await repository.PayProfileChangeRequests
            .FirstOrDefaultAsync(r => r.Id == id && r.State == PayProfileChangeState.Scheduled, ct);
        if (request is null)
            return false;
        if (request.LastApplyError == error)
            return true;

        request.LastApplyError = error.Length > Payroll.Domain.Entities.PayProfileChangeRequest.NoteMaxLength
            ? error[..Payroll.Domain.Entities.PayProfileChangeRequest.NoteMaxLength]
            : error;
        try
        {
            await repository.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
