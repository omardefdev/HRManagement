using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>
/// HCM-028 (G2): the single way a recruitment application becomes Rejected. Records when and why, so the appeal
/// window (<see cref="RecruitmentAppealWorkflowRules.GetAppealError"/>) never depends on <c>ReviewedAt</c>, which
/// later operations overwrite.
/// </summary>
public static class ApplicationRejection
{
    public const int MaxNoteLength = 1000;

    public static void Apply(RecruitmentApplication app, RecruitmentRejectionKind kind, string? note, DateTime nowUtc)
    {
        app.Status = RecruitmentApplicationStatus.Rejected;
        app.ReviewedAt = nowUtc;
        app.RejectedAt = nowUtc;
        app.RejectionKind = kind;
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        app.RejectionNote = trimmed is { Length: > MaxNoteLength } ? trimmed[..MaxNoteLength] : trimmed;
        app.RankBeforeRejection = app.Rank;

        // A new rejection after an upheld appeal is a new decision and may be appealed again.
        if (app.AppealStatus == RecruitmentAppealStatus.Decided)
            app.AppealStatus = RecruitmentAppealStatus.None;
    }
}
