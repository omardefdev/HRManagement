using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-60: a test record reaches a status through the state machine's own transitions (never by assigning it), with a stand-in
/// engine instance for the submission.
/// </summary>
internal static class SeparationTestJourney
{
    private static readonly SeparationStatus[] ToExecuted =
        [SeparationStatus.PendingApproval, SeparationStatus.Approved, SeparationStatus.Executing];

    public static void MoveTo(SeparationRecord record, SeparationStatus target, Guid? actor = null)
    {
        if (record.Status == target)
            return;
        var path = target switch
        {
            SeparationStatus.PendingApproval => ToExecuted[..1],
            SeparationStatus.Approved => ToExecuted[..2],
            SeparationStatus.Rejected => [SeparationStatus.PendingApproval, SeparationStatus.Rejected],
            SeparationStatus.Executing => ToExecuted,
            SeparationStatus.AwaitingIntervention => [.. ToExecuted, SeparationStatus.AwaitingIntervention],
            SeparationStatus.Completed => [.. ToExecuted, SeparationStatus.Completed],
            SeparationStatus.Cancelled => [SeparationStatus.Cancelled],
            _ => Array.Empty<SeparationStatus>()
        };
        // Walk on from where the record already is on the path.
        foreach (var next in path.Skip(Array.IndexOf(path, record.Status) + 1))
        {
            SeparationWorkflowRules.Transition(
                record,
                next,
                actor ?? Guid.NewGuid(),
                next is SeparationStatus.Cancelled or SeparationStatus.Rejected ? "اختبار" : null,
                next == SeparationStatus.PendingApproval
                    ? new SeparationSubmission(Guid.NewGuid(), "SEPARATION-APPROVAL", record.SubmissionNo + 1)
                    : null);
        }
    }
}
