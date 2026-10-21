using System.Globalization;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;

namespace HR.Core.Web.ViewModels;

/// <summary>HCM-026: criteria versions panel on the requisition details page.</summary>
public class AssessmentCriteriaPanelViewModel
{
    public Guid RequisitionId { get; set; }
    public RequisitionStatus RequisitionStatus { get; set; }
    public IReadOnlyList<AssessmentCriteriaVersionRow> Versions { get; set; } = [];
    public AssessmentCriteriaVersionRow? Active => Versions.FirstOrDefault(v => v.Criteria.IsActive);
    public bool CanWrite { get; set; }
    public bool CanApprove { get; set; }

    public bool CanCreateVersion => CanWrite
        && RequisitionStatus is RequisitionStatus.Draft or RequisitionStatus.PendingApproval
            or RequisitionStatus.Approved or RequisitionStatus.Published
        && (Active?.Criteria.LockedAt is null || CanApprove);

    public bool RequiresChangeReason => Active?.Criteria.LockedAt is not null;

    /// <summary>Rows pre-filled in the "new version" editor (copy of the active version, keys preserved).</summary>
    public IReadOnlyList<AssessmentCriteriaItem> EditorSeed =>
        Active?.Items is { Count: > 0 } items
            ? items
            : [new AssessmentCriteriaItem("item-1", string.Empty, 100m, 100m, AssessmentStage.Interview)];
}

public record AssessmentCriteriaVersionRow(RecruitmentAssessmentCriteria Criteria, IReadOnlyList<AssessmentCriteriaItem> Items);

/// <summary>HCM-026: posted "new criteria version" form. Numbers are posted as invariant strings.</summary>
public class AssessmentCriteriaCreateForm
{
    public Guid RequisitionId { get; set; }
    public string? Title { get; set; }
    public string? ChangeReason { get; set; }
    /// <summary>Bound with explicit <c>Items.Index</c> tokens so rows can be added/removed client-side.</summary>
    public List<AssessmentCriteriaItemInput> Items { get; set; } = [];
}

public class AssessmentCriteriaItemInput
{
    public string? Key { get; set; }
    public string? NameAr { get; set; }
    public string? Weight { get; set; }
    public string? MaxScore { get; set; }
    public AssessmentStage Stage { get; set; } = AssessmentStage.Other;
    public bool IsMandatory { get; set; }
    public string? Description { get; set; }
}

/// <summary>HCM-026: culture-independent decimal parsing for numbers posted from number inputs.</summary>
public static class InvariantDecimal
{
    public static bool TryParse(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var normalized = new string(raw.Trim().Select(ch => ch switch
        {
            >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
            >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
            '٫' or ',' => '.',
            _ => ch
        }).ToArray());

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>HCM-026: structured scoring sheet on the application details page.</summary>
public class AssessmentScoringPanelViewModel
{
    public Guid ApplicationId { get; set; }
    public HR.Core.Infrastructure.Services.Recruitment.AssessmentSummary Summary { get; set; } =
        HR.Core.Infrastructure.Services.Recruitment.AssessmentSummary.Empty(1);
    public bool CanScore { get; set; }
    public string? CannotScoreReason { get; set; }
    public IReadOnlyDictionary<string, decimal> MyScores { get; set; } = new Dictionary<string, decimal>();
    public string? MyNotes { get; set; }
    public Guid? CurrentUserId { get; set; }
    public IReadOnlyDictionary<Guid, string> AssessorNames { get; set; } = new Dictionary<Guid, string>();
    /// <summary>Set when the criteria contain Interview-stage items and no interview was attended yet.</summary>
    public string? InterviewGateHint { get; set; }
}

/// <summary>HCM-026: posted scores keyed by criteria item key; values are invariant strings.</summary>
public class SubmitScoresForm
{
    public Guid ApplicationId { get; set; }
    public Dictionary<string, string?> Scores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Notes { get; set; }
    public bool NoConflictDeclared { get; set; }
}

/// <summary>HCM-026: posted interview schedule form.</summary>
public class ScheduleInterviewForm
{
    public Guid ApplicationId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public InterviewMode Mode { get; set; } = InterviewMode.InPerson;
    public string? Location { get; set; }
    public List<Guid> PanelEmployeeIds { get; set; } = [];
    public string? PanelNotes { get; set; }
}
