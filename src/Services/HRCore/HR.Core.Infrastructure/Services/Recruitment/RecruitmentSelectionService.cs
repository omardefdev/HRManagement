using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

public sealed class RecruitmentSelectionService(HRCoreDbContext db)
{
    public const string DuplicateRankMessage = "الترتيب مستخدم لمرشح آخر على نفس طلب الشاغر.";

    /// <summary>HCM-031 (J4): the results approval of the competition replaces the PLAN-28 committee gate.</summary>
    public const string CompetitionRankingMessage =
        "يُحدَّد الترتيب في المسابقة باعتماد النتائج ونشرها ثم إنهاء المسابقة، لا يدوياً ولا بلجنة اختيار.";

    public async Task<(bool Success, string? Error)> DeclareCoiAsync(
        RecruitmentApplication application,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var app = await LoadApplicationAsync(application.Id, ct);
        if (app is null) return (false, "الطلب غير موجود.");

        app.ConflictOfInterestDeclared = true;
        app.ConflictOfInterestDeclaredAt = DateTime.UtcNow;

        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(RecruitmentApplication),
            EntityId = app.Id,
            Action = "DeclareCoi",
            UserId = actorUserId,
            ChangedFields = """{"conflictOfInterestDeclared":true}"""
        });

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Legacy — not exposed; PLAN-26. Free-form single score kept only for data migration tooling.
    /// Use <see cref="RecruitmentScoringService.SubmitScoresAsync"/> for structured multi-assessor scoring.
    /// </summary>
    internal async Task<(bool Success, string? Error)> RecordAssessmentAsync(
        Guid applicationId,
        Guid criteriaVersionId,
        decimal score,
        CancellationToken ct = default)
    {
        var app = await LoadApplicationAsync(applicationId, ct);
        if (app is null) return (false, "الطلب غير موجود.");

        var criteria = await db.RecruitmentAssessmentCriteria
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == criteriaVersionId && c.RequisitionId == app.RequisitionId, ct);
        if (criteria is null)
            return (false, "إصدار المعايير غير صالح لهذا الطلب.");

        app.AssessmentScore = score;
        app.CriteriaVersionId = criteriaVersionId;

        if (app.Status == RecruitmentApplicationStatus.InterviewScheduled)
        {
            var gate = SelectionWorkflowRules.ValidateTransitionGate(app, RecruitmentApplicationStatus.Assessed);
            if (gate is null)
            {
                app.Status = RecruitmentApplicationStatus.Assessed;
                app.ReviewedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Assigns a rank to an Assessed/Ranked application. HCM-028 (C): when the requisition requires committee
    /// approval, the latest approved Ranking/Final session must recommend (or reserve) the candidate and the rank
    /// must equal its proposed rank — unless <paramref name="rankOverrideReason"/> (at least 20 chars) is given,
    /// which is stored on the application and audited.
    /// </summary>
    public async Task<(bool Success, string? Error)> AssignRankAsync(
        Guid applicationId,
        int rank,
        string? rankOverrideReason = null,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        if (rank < 1)
            return (false, "الترتيب يجب أن يكون موجباً.");

        var app = await LoadApplicationAsync(applicationId, ct);
        if (app is null) return (false, "الطلب غير موجود.");
        // HCM-031 (J4): competition ranks come from the approved, published results and finalization only.
        if (app.CompetitionId is not null)
            return (false, CompetitionRankingMessage);

        var gate = app.RequisitionId is Guid requisitionId
            ? await GetCommitteeRankingGateAsync(requisitionId, ct)
            : null;
        var error = ValidateRanking(app, rank, rankOverrideReason, gate);
        if (error is not null) return (false, error);

        var duplicate = await db.RecruitmentApplications.AnyAsync(
            a => a.RequisitionId == app.RequisitionId && a.Id != app.Id && a.Rank == rank, ct);
        if (duplicate)
            return (false, DuplicateRankMessage);

        ApplyRank(app, rank, gate is { Required: true } ? gate.DecisionFor(app.Id) : null, rankOverrideReason, actorUserId);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// HCM-028 (C): applies the proposed ranks of every Recommend/Reserve decision of the latest approved
    /// Ranking/Final session in one transaction, with the same validation as <see cref="AssignRankAsync"/>.
    /// </summary>
    public async Task<(bool Success, string? Error, int RankedCount)> ApplyCommitteeRankingAsync(
        Guid requisitionId,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        if (await db.RecruitmentRequisitions.AnyAsync(r => r.Id == requisitionId && r.CompetitionLineId != null, ct))
            return (false, CompetitionRankingMessage, 0);

        var gate = await GetCommitteeRankingGateAsync(requisitionId, ct);
        if (gate.ApprovedSession is null)
            return (false, SelectionCommitteeRules.GateNoApprovedSessionMessage, 0);

        var decisions = gate.ApprovedSession.Decisions
            .Where(d => d.Recommendation != CommitteeRecommendation.Reject && d.ProposedRank is > 0)
            .ToList();
        if (decisions.Count == 0)
            return (false, "لا توجد توصيات بترتيب مقترح في الجلسة المعتمدة.", 0);

        var targetIds = decisions.Select(d => d.ApplicationId).ToList();
        var apps = await db.RecruitmentApplications
            .Include(a => a.ActiveOfferLetter)
            .Where(a => targetIds.Contains(a.Id) && a.RequisitionId == requisitionId)
            .ToDictionaryAsync(a => a.Id, ct);

        foreach (var decision in decisions)
        {
            if (!apps.TryGetValue(decision.ApplicationId, out var app))
                return (false, "أحد المرشحين في توصيات اللجنة لم يعد على طلب الشاغر.", 0);
            var error = ValidateRanking(app, decision.ProposedRank!.Value, null, gate with { Required = true });
            if (error is not null)
                return (false, $"{app.ApplicantFullName}: {error}", 0);
        }

        var proposedRanks = decisions.Select(d => d.ProposedRank!.Value).ToList();
        var outsider = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.RequisitionId == requisitionId
                && !targetIds.Contains(a.Id)
                && a.Rank != null
                && proposedRanks.Contains(a.Rank.Value))
            .Select(a => new { a.ApplicantFullName, a.Rank })
            .FirstOrDefaultAsync(ct);
        if (outsider is not null)
            return (false, $"الترتيب {outsider.Rank} مستخدم للمرشح «{outsider.ApplicantFullName}» خارج توصيات اللجنة.", 0);

        var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            // Clear first so swapping ranks between candidates never trips the unique (requisition, rank) index.
            foreach (var app in apps.Values.Where(a => a.Rank is not null))
                app.Rank = null;
            await db.SaveChangesAsync(ct);

            foreach (var decision in decisions)
                ApplyRank(apps[decision.ApplicationId], decision.ProposedRank!.Value, decision, null, actorUserId);

            db.AuditLogs.Add(new EntityAuditLog
            {
                EntityType = nameof(RecruitmentRequisition),
                EntityId = requisitionId,
                Action = "ApplyCommitteeRanking",
                UserId = actorUserId,
                ChangedFields = JsonSerializer.Serialize(new
                {
                    sessionId = gate.ApprovedSession.Id,
                    ranks = decisions.Select(d => new { applicationId = d.ApplicationId, rank = d.ProposedRank })
                })
            });
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }

        return (true, null, decisions.Count);
    }

    /// <summary>
    /// HCM-028 (C): whether ranking on the requisition is committee-gated, and the latest approved Ranking/Final
    /// session (with its decisions) whether or not the gate is on.
    /// </summary>
    public async Task<CommitteeRankingGate> GetCommitteeRankingGateAsync(Guid requisitionId, CancellationToken ct = default)
    {
        var required = await db.RecruitmentRequisitions.AsNoTracking()
            .Where(r => r.Id == requisitionId)
            .Select(r => r.RequiresCommitteeApproval)
            .FirstOrDefaultAsync(ct);
        var session = await db.SelectionCommitteeSessions.AsNoTracking()
            .Include(s => s.Decisions)
            .Where(s => s.RequisitionId == requisitionId
                && s.ApprovedAt != null
                && (s.Purpose == CommitteePurpose.Ranking || s.Purpose == CommitteePurpose.Final))
            .OrderByDescending(s => s.ApprovedAt)
            .FirstOrDefaultAsync(ct);
        return new CommitteeRankingGate(required, session);
    }

    private static string? ValidateRanking(
        RecruitmentApplication app,
        int rank,
        string? rankOverrideReason,
        CommitteeRankingGate? gate)
    {
        if (!SelectionCommitteeRules.CandidateStatuses.Contains(app.Status))
            return SelectionCommitteeRules.NotAssessedMessage;
        if (!RecruitmentAppealWorkflowRules.CanRank(app))
            return "لا يمكن تعيين الترتيب أثناء استئناف معلق.";
        if (app.RequisitionId is null)
            return "الترتيب يتطلب طلب شاغر.";

        if (gate is not { Required: true })
            return null;
        if (gate.ApprovedSession is null)
            return SelectionCommitteeRules.GateNoApprovedSessionMessage;
        return SelectionCommitteeRules.ValidateRankAgainstDecision(gate.DecisionFor(app.Id), rank, rankOverrideReason);
    }

    private void ApplyRank(
        RecruitmentApplication app,
        int rank,
        SelectionCommitteeDecision? decision,
        string? rankOverrideReason,
        Guid? actorUserId)
    {
        var now = DateTime.UtcNow;
        app.Rank = rank;
        app.RankedAt = now;

        if (decision is not null)
        {
            if (decision.ProposedRank == rank)
            {
                app.RankOverrideReason = null;
                app.RankOverriddenBy = null;
                app.RankOverriddenAt = null;
            }
            else
            {
                app.RankOverrideReason = rankOverrideReason!.Trim();
                app.RankOverriddenBy = actorUserId;
                app.RankOverriddenAt = now;
                db.AuditLogs.Add(new EntityAuditLog
                {
                    EntityType = nameof(RecruitmentApplication),
                    EntityId = app.Id,
                    Action = "RankOverride",
                    UserId = actorUserId,
                    ChangedFields = JsonSerializer.Serialize(new
                    {
                        sessionId = decision.SessionId,
                        proposedRank = decision.ProposedRank,
                        rank,
                        reason = app.RankOverrideReason
                    })
                });
            }
        }

        if (app.Status == RecruitmentApplicationStatus.Assessed)
        {
            var transitionGate = SelectionWorkflowRules.ValidateTransitionGate(app, RecruitmentApplicationStatus.Ranked);
            if (transitionGate is null)
            {
                app.Status = RecruitmentApplicationStatus.Ranked;
                app.ReviewedAt = now;
            }
        }
    }

    /// <summary>
    /// HCM-028 (H3): checks a rank correction ordered by an upheld appeal before the reviewer's decision is approved
    /// (the effect is applied after the workflow completes, so it must not fail there).
    /// </summary>
    public async Task<string?> ValidateAppealRankCorrectionAsync(
        Guid applicationId,
        Guid? requisitionId,
        int rank,
        CancellationToken ct = default)
    {
        if (rank < 1)
            return "الترتيب يجب أن يكون موجباً.";
        if (requisitionId is null)
            return "الترتيب يتطلب طلب شاغر.";
        var duplicate = await db.RecruitmentApplications.AnyAsync(
            a => a.RequisitionId == requisitionId && a.Id != applicationId && a.Rank == rank, ct);
        return duplicate ? DuplicateRankMessage : null;
    }

    /// <summary>
    /// HCM-028 (H3): stages the rank ordered by an upheld appeal (no save). The independent reviewer's decision
    /// outranks the committee's, so the committee gate of <see cref="AssignRankAsync"/> is not applied; the rationale is
    /// stored as the rank override reason and audited as <c>AppealRankCorrection</c>.
    /// </summary>
    internal async Task<string?> StageAppealRankCorrectionAsync(
        RecruitmentApplication app,
        int rank,
        string rationale,
        Guid appealId,
        int? rankBeforeRejection,
        Guid? actorUserId,
        CancellationToken ct)
    {
        var error = await ValidateAppealRankCorrectionAsync(app.Id, app.RequisitionId, rank, ct);
        if (error is not null)
            return error;

        var gate = await GetCommitteeRankingGateAsync(app.RequisitionId!.Value, ct);
        var now = DateTime.UtcNow;
        var reason = rationale.Trim();
        app.Rank = rank;
        app.RankedAt = now;
        app.RankOverrideReason = reason.Length > 1000 ? reason[..1000] : reason;
        app.RankOverriddenBy = actorUserId;
        app.RankOverriddenAt = now;
        if (app.Status == RecruitmentApplicationStatus.Assessed
            && SelectionWorkflowRules.ValidateTransitionGate(app, RecruitmentApplicationStatus.Ranked) is null)
        {
            app.Status = RecruitmentApplicationStatus.Ranked;
            app.ReviewedAt = now;
        }

        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(RecruitmentApplication),
            EntityId = app.Id,
            Action = "AppealRankCorrection",
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new
            {
                appealId,
                rankBeforeRejection,
                rank,
                committeeProposedRank = gate.DecisionFor(app.Id)?.ProposedRank,
                reason = app.RankOverrideReason
            })
        });
        return null;
    }

    /// <summary>HCM-028 (A1): ranking has started once any application on the requisition holds a rank.</summary>
    public Task<bool> HasRankingStartedAsync(Guid requisitionId, CancellationToken ct = default) =>
        db.RecruitmentApplications.AnyAsync(a => a.RequisitionId == requisitionId && a.Rank != null, ct);

    /// <summary>HCM-028 (A1): toggles <c>RequiresCommitteeApproval</c> until the first rank is assigned.</summary>
    public async Task<(bool Success, string? Error)> SetCommitteeApprovalRequiredAsync(
        Guid requisitionId,
        bool required,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        var requisition = await db.RecruitmentRequisitions.FirstOrDefaultAsync(r => r.Id == requisitionId, ct);
        if (requisition is null) return (false, "طلب الشاغر غير موجود.");
        if (requisition.RequiresCommitteeApproval == required) return (true, null);
        if (await HasRankingStartedAsync(requisitionId, ct))
            return (false, SelectionCommitteeRules.RequirementFrozenMessage);

        requisition.RequiresCommitteeApproval = required;
        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = nameof(RecruitmentRequisition),
            EntityId = requisition.Id,
            Action = "SetCommitteeApprovalRequired",
            UserId = actorUserId,
            ChangedFields = $$"""{"requiresCommitteeApproval":{{(required ? "true" : "false")}}}"""
        });
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<IReadOnlyList<RecruitmentApplication>> GetRankedCandidatesAsync(
        Guid requisitionId,
        CancellationToken ct = default) =>
        await db.RecruitmentApplications
            .AsNoTracking()
            .Where(a => a.RequisitionId == requisitionId && a.Rank != null)
            .OrderBy(a => a.Rank)
            .ThenBy(a => a.ApplicantFullName)
            .ToListAsync(ct);

    public async Task<(bool Success, string? Error)> TransitionStatusAsync(
        Guid applicationId,
        RecruitmentApplicationStatus to,
        bool legacyHireWithoutOffer,
        string? rejectionNote = null,
        string? eligibilityOverrideReason = null,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        if (SelectionWorkflowRules.SystemOnlyTargets.Contains(to))
            return (false, SelectionWorkflowRules.SystemOnlyTargetMessage(to));

        var app = await LoadApplicationAsync(applicationId, ct);
        if (app is null) return (false, "الطلب غير موجود.");

        if (!SelectionWorkflowRules.CanTransition(app, to, legacyHireWithoutOffer))
        {
            var gate = SelectionWorkflowRules.ValidateTransitionGate(app, to)
                ?? "لا يمكن تنفيذ الانتقال المطلوب للحالة الحالية.";
            return (false, gate);
        }

        // HCM-030 (D4): eligibility flags never reject, but shortlisting a flagged application needs a recorded reason.
        var flags = RecruitmentEligibilityRules.Parse(app.EligibilityFlags);
        if (to == RecruitmentApplicationStatus.Shortlisted && flags.Count > 0)
        {
            if (!RecruitmentEligibilityRules.IsValidOverrideReason(eligibilityOverrideReason))
                return (false, RecruitmentEligibilityRules.OverrideReasonRequiredMessage);

            var reason = eligibilityOverrideReason!.Trim();
            app.EligibilityOverrideReason = reason.Length > 1000 ? reason[..1000] : reason;
            db.AuditLogs.Add(new EntityAuditLog
            {
                EntityType = nameof(RecruitmentApplication),
                EntityId = app.Id,
                Action = RecruitmentEligibilityRules.OverrideAuditAction,
                UserId = actorUserId,
                ChangedFields = JsonSerializer.Serialize(new { flags, reason = app.EligibilityOverrideReason })
            });
        }

        if (to == RecruitmentApplicationStatus.Rejected)
        {
            ApplicationRejection.Apply(app, RecruitmentRejectionKind.SelectionDecision, rejectionNote, DateTime.UtcNow);
        }
        else
        {
            app.Status = to;
            app.ReviewedAt = DateTime.UtcNow;
            if (to == RecruitmentApplicationStatus.UnderReview)
                app.ScreenedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public const string AcceptedOfferWithdrawMessage = "تم قبول العرض؛ للانسحاب يرجى التواصل مع جهة التوظيف.";
    public const string CannotWithdrawMessage = "لا يمكن الانسحاب من الطلب في حالته الحالية.";
    public const int MaxWithdrawReasonLength = 500;

    /// <summary>
    /// HCM-030 (PLAN-30 I, DECISIONS I): the candidate withdraws on <c>/careers/my</c>. Allowed from every open status before
    /// <see cref="RecruitmentApplicationStatus.OfferAccepted"/> (an accepted offer has no workflow exit: the candidate is
    /// told to contact the employer). The active offer is settled first in the same save
    /// (<see cref="RecruitmentOfferService.SettleForWithdrawalAsync"/>: Issued → Declined by the candidate, Draft/PendingApproval
    /// → Revoked with the approval cancelled), then the application becomes Withdrawn. Audited <c>WithdrawByCandidate</c>
    /// (actor Candidate, no user). <paramref name="offers"/> is a parameter, not a dependency, so this service keeps its
    /// one-argument constructor. Returns the settled offer id so the caller can run the HCM-027 next-candidate draft.
    /// </summary>
    public async Task<(bool Success, string? Error, Guid? SettledOfferId)> WithdrawByCandidateAsync(
        Guid applicationId,
        string? reason,
        RecruitmentOfferService offers,
        CancellationToken ct = default)
    {
        var trimmed = reason?.Trim();
        if (trimmed is null || trimmed.Length < OfferWorkflowRules.MinDeclineReasonLength)
            return (false, $"سبب الانسحاب مطلوب ({OfferWorkflowRules.MinDeclineReasonLength} أحرف على الأقل).", null);
        if (trimmed.Length > MaxWithdrawReasonLength)
            return (false, $"سبب الانسحاب يجب ألا يتجاوز {MaxWithdrawReasonLength} حرف.", null);

        var app = await LoadApplicationAsync(applicationId, ct);
        if (app is null) return (false, "الطلب غير موجود.", null);
        // Terminal first: a Hired application keeps its accepted offer, but there is nothing left to withdraw from.
        if (SelectionWorkflowRules.TerminalStatuses.Contains(app.Status))
            return (false, CannotWithdrawMessage, null);
        if (app.Status is RecruitmentApplicationStatus.OfferAccepted
            || app.ActiveOfferLetter?.Status == OfferStatus.Accepted)
            return (false, AcceptedOfferWithdrawMessage, null);
        if (!SelectionWorkflowRules.CanTransition(app, RecruitmentApplicationStatus.Withdrawn))
            return (false, CannotWithdrawMessage, null);

        var now = DateTime.UtcNow;
        var fromStatus = app.Status;
        Guid? settledOfferId = null;
        if (app.ActiveOfferLetter is { Status: OfferStatus.Draft or OfferStatus.PendingApproval or OfferStatus.Issued } offer)
        {
            settledOfferId = offer.Id;
            await offers.SettleForWithdrawalAsync(offer, trimmed, now, ct);
        }
        else
        {
            app.Notes = string.IsNullOrWhiteSpace(app.Notes)
                ? RecruitmentOfferService.WithdrawalReasonPrefix + trimmed
                : $"{app.Notes}\n{RecruitmentOfferService.WithdrawalReasonPrefix}{trimmed}";
        }

        // SettleApplication leaves the status alone for an offer that is not the active one; set it in every case.
        app.Status = RecruitmentApplicationStatus.Withdrawn;
        app.ReviewedAt = now;
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = app.TenantId,
            EntityType = nameof(RecruitmentApplication),
            EntityId = app.Id,
            Action = "WithdrawByCandidate",
            UserId = null,
            ChangedFields = JsonSerializer.Serialize(new
            {
                actor = "Candidate",
                fromStatus = fromStatus.ToString(),
                settledOfferId,
                reason = trimmed
            })
        });

        await db.SaveChangesAsync(ct);
        return (true, null, settledOfferId);
    }

    private Task<RecruitmentApplication?> LoadApplicationAsync(Guid id, CancellationToken ct) =>
        db.RecruitmentApplications
            .Include(a => a.ActiveOfferLetter)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
}
