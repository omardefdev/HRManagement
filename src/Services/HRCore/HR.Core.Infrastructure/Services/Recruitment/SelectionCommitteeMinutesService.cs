using System.Text.Json;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>HCM-028 (D): what the committee minutes PDF prints — taken from the locked session data.</summary>
public sealed record SelectionCommitteeMinutesData(
    Guid SessionId,
    string RequisitionNumber,
    string JobTitle,
    DateTime CommitteeDate,
    CommitteePurpose Purpose,
    DateTime? SubmittedAt,
    DateTime? ApprovedAt,
    IReadOnlyList<SelectionCommitteeMinutesMember> Members,
    IReadOnlyList<SelectionCommitteeMinutesCandidate> Candidates,
    IReadOnlyList<SelectionCommitteeMinutesApproval> Approvals);

public sealed record SelectionCommitteeMinutesMember(string Name, bool IsChair, bool Recused);

public sealed record SelectionCommitteeMinutesCandidate(
    string Name,
    decimal? Score,
    CommitteeVoteTally Votes,
    CommitteeRecommendation? Recommendation,
    int? ProposedRank,
    string? Rationale);

public sealed record SelectionCommitteeMinutesApproval(
    int Level,
    string StepName,
    WorkflowStepStatus Status,
    string? ActorName,
    DateTime ActionAt,
    string? Comments);

public sealed class SelectionCommitteeMinutesService(HRCoreDbContext db, SelectionCommitteeService committeeService)
{
    public const string NotSubmittedMessage = "المحضر متاح بعد تقديم الجلسة للاعتماد فقط.";

    public async Task<(SelectionCommitteeMinutesData? Minutes, string? Error)> GetMinutesAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        var workspace = await committeeService.GetWorkspaceAsync(sessionId, ct);
        if (workspace is null) return (null, SelectionCommitteeService.SessionNotFoundMessage);
        if (workspace.Session.SubmittedAt is null) return (null, NotSubmittedMessage);

        var session = workspace.Session;
        var members = workspace.Members
            .Select(m => new SelectionCommitteeMinutesMember(
                m.Employee?.FullName ?? m.EmployeeId.ToString(),
                SelectionCommitteeRules.IsChairRole(m.Role),
                m.Recused))
            .ToList();

        var candidates = workspace.Candidates
            .Select(c =>
            {
                var decision = workspace.DecisionFor(c.Id);
                return new SelectionCommitteeMinutesCandidate(
                    c.ApplicantFullName,
                    c.AssessmentScore,
                    workspace.TallyFor(c.Id),
                    decision?.Recommendation,
                    decision?.ProposedRank,
                    decision?.Rationale);
            })
            .OrderBy(c => c.Recommendation == CommitteeRecommendation.Reject)
            .ThenBy(c => c.ProposedRank ?? int.MaxValue)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        var approvals = new List<SelectionCommitteeMinutesApproval>();
        if (session.WorkflowInstanceId is Guid workflowId)
        {
            var historyJson = await db.WorkflowInstances.AsNoTracking()
                .Where(i => i.Id == workflowId)
                .Select(i => i.StepHistoryJson)
                .FirstOrDefaultAsync(ct);
            var history = string.IsNullOrWhiteSpace(historyJson)
                ? []
                : JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(historyJson) ?? [];
            approvals.AddRange(history
                .OrderBy(h => h.ActionAt)
                .Select(h => new SelectionCommitteeMinutesApproval(
                    h.Level, h.StepName, h.Status, h.ActorName, h.ActionAt, h.Comments)));
        }

        return (new SelectionCommitteeMinutesData(
            session.Id,
            session.Requisition?.RequisitionNumber ?? "—",
            session.Requisition?.JobTitle ?? "—",
            session.CommitteeDate,
            session.Purpose,
            session.SubmittedAt,
            session.ApprovedAt,
            members,
            candidates,
            approvals), null);
    }
}
