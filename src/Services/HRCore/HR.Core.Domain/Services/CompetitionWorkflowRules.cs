using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;

namespace HR.Core.Domain.Services;

/// <summary>HCM-031 (PLAN-31): legal inputs of a competition, all taken from the approved legal reference (D-2).</summary>
public sealed record CompetitionLegalInputs(
    int? ObjectionWindowDays,
    int? ReserveValidityMonths,
    int? MaxLinesPerCandidate,
    decimal? PassMark,
    decimal? ReconcileThreshold,
    CompetitionReconcileRule? ReconcileRule,
    IReadOnlyList<CompetitionTieBreak>? TieBreakOrder);

/// <summary>HCM-031: what a requisition looks like to the attach check.</summary>
public sealed record CompetitionAttachCandidate(
    RequisitionStatus Status,
    DateTime? PublishedAt,
    Guid? PublicJobPostingId,
    Guid? InternalVacancyId,
    Guid? CompetitionLineId,
    string? PositionGradeCode,
    Guid? PositionJobCatalogEntryId);

/// <summary>HCM-031: one line as seen by the submit-for-approval check.</summary>
public sealed record CompetitionLineSubmitView(
    int LineNo,
    string Title,
    int RequisitionCount,
    IReadOnlyList<IReadOnlyList<AssessmentCriteriaItem>?> RequisitionCriteria);

/// <summary>
/// HCM-031 (PLAN-31): pure rules of the competition lifecycle — status transitions, legal-input sanity ranges (not legal
/// values), requisition attachment and the submit-for-approval checks.
/// </summary>
public static class CompetitionWorkflowRules
{
    public const int MinObjectionWindowDays = 1;
    public const int MaxObjectionWindowDays = 60;
    public const int MinReserveValidityMonths = 0;
    public const int MaxReserveValidityMonths = 36;
    public const decimal MaxFinalScore = AssessmentCriteriaRules.RequiredWeightTotal;
    public const int MinCancellationReasonLength = 10;

    public const string LegalInputsMissingMessage =
        "يجب إدخال كل المعاملات القانونية للمسابقة من المرجع المعتمد (مدة الاعتراض، صلاحية الاحتياط، الحد الأقصى للأسطر، درجة النجاح، عتبة التصحيح الثالث، قاعدة التسوية، ترتيب كسر التعادل).";
    public const string NotEditableMessage = "لا يمكن تعديل المسابقة إلا وهي مسودة.";
    public const string RequisitionNotApprovedMessage = "لا يمكن ربط طلب شاغر غير معتمد بالمسابقة.";
    public const string RequisitionAlreadyPublishedMessage = "لا يمكن ربط طلب شاغر سبق نشره أو له إعلان أو شاغر داخلي.";
    public const string RequisitionAlreadyAttachedMessage = "طلب الشاغر مربوط بسطر مسابقة آخر.";
    public const string GradeMismatchMessage = "درجة المنصب الممول لا تطابق درجة سطر المسابقة.";
    public const string JobMismatchMessage = "وظيفة المنصب الممول لا تطابق وظيفة سطر المسابقة في الدليل.";
    public const string AttachedRequisitionPublishMessage = "طلب الشاغر مربوط بمسابقة ويُنشر بإعلانها، لا منفرداً.";
    public const string AttachedRequisitionChangeMessage = "طلب الشاغر مربوط بمسابقة قائمة — افصله عن المسابقة أو ألغِ المسابقة أولاً.";
    public const string NoLinesMessage = "يجب إضافة سطر واحد على الأقل إلى المسابقة.";
    public const string CriteriaShapeMismatchMessage =
        "يجب أن تتطابق بنود معايير التقييم (المفتاح والمرحلة والوزن والحد الأعلى) في كل طلبات الشواغر وكل أسطر المسابقة.";
    public const string CriteriaNeedExamMessage = "يجب أن تتضمن معايير التقييم بنداً واحداً على الأقل من مرحلة «الاختبار».";

    private static readonly Dictionary<RecruitmentCompetitionStatus, RecruitmentCompetitionStatus[]> Transitions = new()
    {
        [RecruitmentCompetitionStatus.Draft] = [RecruitmentCompetitionStatus.PendingApproval],
        [RecruitmentCompetitionStatus.PendingApproval] = [RecruitmentCompetitionStatus.Approved, RecruitmentCompetitionStatus.Draft],
        [RecruitmentCompetitionStatus.Approved] = [RecruitmentCompetitionStatus.Announced],
        [RecruitmentCompetitionStatus.Announced] = [RecruitmentCompetitionStatus.IntakeClosed],
        [RecruitmentCompetitionStatus.IntakeClosed] = [RecruitmentCompetitionStatus.Examining],
        [RecruitmentCompetitionStatus.Examining] = [RecruitmentCompetitionStatus.ResultsDraft],
        [RecruitmentCompetitionStatus.ResultsDraft] = [RecruitmentCompetitionStatus.ResultsPendingApproval],
        [RecruitmentCompetitionStatus.ResultsPendingApproval] =
            [RecruitmentCompetitionStatus.ResultsPublished, RecruitmentCompetitionStatus.ResultsDraft],
        [RecruitmentCompetitionStatus.ResultsPublished] = [RecruitmentCompetitionStatus.Finalized],
        [RecruitmentCompetitionStatus.Finalized] = [],
        [RecruitmentCompetitionStatus.Cancelled] = []
    };

    public static bool CanTransition(RecruitmentCompetitionStatus from, RecruitmentCompetitionStatus to) =>
        (to == RecruitmentCompetitionStatus.Cancelled && CanCancel(from))
        || (Transitions.TryGetValue(from, out var targets) && targets.Contains(to));

    public static bool CanEdit(RecruitmentCompetitionStatus status) => status == RecruitmentCompetitionStatus.Draft;

    public static bool CanCancel(RecruitmentCompetitionStatus status) =>
        status is not (RecruitmentCompetitionStatus.Finalized or RecruitmentCompetitionStatus.Cancelled);

    /// <summary>Statuses in which an attached requisition belongs to the competition (it cannot be published, closed or cancelled on its own).</summary>
    public static bool HoldsRequisitions(RecruitmentCompetitionStatus status) =>
        status is not (RecruitmentCompetitionStatus.Finalized or RecruitmentCompetitionStatus.Cancelled);

    /// <summary>Applications are accepted while announced and on or before the closing date.</summary>
    public static bool IsIntakeOpen(RecruitmentCompetitionStatus status, DateOnly closingDate, DateTime? intakeClosedAt, DateOnly todayUtc) =>
        status == RecruitmentCompetitionStatus.Announced && intakeClosedAt is null && todayUtc <= closingDate;

    /// <summary>
    /// Sanity ranges of the legal inputs (not legal values). <paramref name="lineCount"/> null skips the upper bound of
    /// <c>MaxLinesPerCandidate</c> (lines are added after creation; the bound is checked at submit).
    /// </summary>
    public static string? ValidateLegalInputs(CompetitionLegalInputs inputs, int? lineCount = null)
    {
        if (inputs.ObjectionWindowDays is null || inputs.ReserveValidityMonths is null
            || inputs.MaxLinesPerCandidate is null || inputs.PassMark is null || inputs.ReconcileThreshold is null
            || inputs.ReconcileRule is null || inputs.TieBreakOrder is null || inputs.TieBreakOrder.Count == 0)
            return LegalInputsMissingMessage;

        if (inputs.ObjectionWindowDays is < MinObjectionWindowDays or > MaxObjectionWindowDays)
            return $"مدة الاعتراض يجب أن تكون بين {MinObjectionWindowDays} و{MaxObjectionWindowDays} يوماً.";
        if (inputs.ReserveValidityMonths is < MinReserveValidityMonths or > MaxReserveValidityMonths)
            return $"صلاحية الاحتياط يجب أن تكون بين {MinReserveValidityMonths} و{MaxReserveValidityMonths} شهراً.";
        if (inputs.MaxLinesPerCandidate < 1)
            return "الحد الأقصى للأسطر لكل متقدم يجب أن يكون 1 على الأقل.";
        if (lineCount is int lines && inputs.MaxLinesPerCandidate > lines)
            return $"الحد الأقصى للأسطر لكل متقدم ({inputs.MaxLinesPerCandidate}) يتجاوز عدد أسطر المسابقة ({lines}).";
        if (inputs.PassMark is <= 0 or > MaxFinalScore)
            return $"درجة النجاح يجب أن تكون أكبر من صفر ولا تتجاوز {MaxFinalScore:0}.";
        if (inputs.ReconcileThreshold <= 0)
            return "عتبة التصحيح الثالث يجب أن تكون أكبر من صفر.";
        if (!Enum.IsDefined(inputs.ReconcileRule.Value))
            return "قاعدة التسوية غير معروفة.";
        if (inputs.TieBreakOrder.Distinct().Count() != inputs.TieBreakOrder.Count
            || inputs.TieBreakOrder.Any(t => !Enum.IsDefined(t)))
            return "ترتيب كسر التعادل يجب أن يتكون من معايير معروفة دون تكرار.";
        return null;
    }

    /// <summary>A line pass mark (optional) is a legal input with the same range as the competition pass mark.</summary>
    public static string? ValidateLinePassMark(decimal? linePassMark) =>
        linePassMark is <= 0 or > MaxFinalScore
            ? $"درجة نجاح السطر يجب أن تكون أكبر من صفر ولا تتجاوز {MaxFinalScore:0}."
            : null;

    public static string FormatTieBreakOrder(IEnumerable<CompetitionTieBreak> order) => string.Join(',', order);

    public static IReadOnlyList<CompetitionTieBreak> ParseTieBreakOrder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];
        var result = new List<CompetitionTieBreak>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse<CompetitionTieBreak>(part, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed) && !result.Contains(parsed))
                result.Add(parsed);
        return result;
    }

    /// <summary>
    /// Attach check (decision 2): only an Approved, never-published requisition with no posting or vacancy of its own,
    /// not attached elsewhere, whose funded position grade equals the line grade (and job catalog entry, when the line sets one).
    /// </summary>
    public static string? ValidateAttach(CompetitionAttachCandidate requisition, CompetitionLine line, Guid? currentLineId = null)
    {
        if (requisition.CompetitionLineId is Guid attached && attached != currentLineId)
            return RequisitionAlreadyAttachedMessage;
        if (requisition.Status != RequisitionStatus.Approved)
            return RequisitionNotApprovedMessage;
        if (requisition.PublishedAt is not null || requisition.PublicJobPostingId is not null || requisition.InternalVacancyId is not null)
            return RequisitionAlreadyPublishedMessage;
        if (!string.Equals(requisition.PositionGradeCode?.Trim(), line.GradeCode.Trim(), StringComparison.OrdinalIgnoreCase))
            return GradeMismatchMessage;
        if (line.JobCatalogEntryId is Guid job && requisition.PositionJobCatalogEntryId != job)
            return JobMismatchMessage;
        return null;
    }

    /// <summary>
    /// Submit check: every line has ≥ 1 requisition; every attached requisition has an active criteria version containing
    /// an Exam item with weights summing to 100; and all of them share one criteria shape (PLAN-31 J2).
    /// </summary>
    public static string? ValidateSubmit(IReadOnlyList<CompetitionLineSubmitView> lines, CompetitionLegalInputs inputs)
    {
        if (lines.Count == 0)
            return NoLinesMessage;

        var legal = ValidateLegalInputs(inputs, lines.Count);
        if (legal is not null)
            return legal;

        string? referenceShape = null;
        foreach (var line in lines.OrderBy(l => l.LineNo))
        {
            if (line.RequisitionCount == 0)
                return $"السطر {line.LineNo} «{line.Title}» لا يتضمن أي طلب شاغر.";
            foreach (var criteria in line.RequisitionCriteria)
            {
                if (criteria is null || criteria.Count == 0)
                    return $"يجب اعتماد معايير التقييم لكل طلبات الشواغر في السطر {line.LineNo} «{line.Title}».";
                var itemsError = AssessmentCriteriaRules.Validate(criteria);
                if (itemsError is not null)
                    return $"السطر {line.LineNo}: {itemsError}";
                if (!criteria.Any(i => i.Stage == AssessmentStage.Exam))
                    return $"السطر {line.LineNo}: {CriteriaNeedExamMessage}";
                var shape = CriteriaShape(criteria);
                referenceShape ??= shape;
                if (shape != referenceShape)
                    return CriteriaShapeMismatchMessage;
            }
        }
        return null;
    }

    /// <summary>Canonical key of a criteria set: items ordered by key, with stage, weight and max score.</summary>
    public static string CriteriaShape(IEnumerable<AssessmentCriteriaItem> items) =>
        string.Join('|', items
            .OrderBy(i => i.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(i => FormattableString.Invariant($"{i.Key.Trim().ToUpperInvariant()}:{i.Stage}:{i.Weight:0.####}:{i.MaxScore:0.####}")));

    /// <summary>The objection deadline (inclusive, UTC) of the first publication; null before the first publication.</summary>
    public static DateTime? ObjectionDeadlineUtc(DateTime? resultsFirstPublishedAt, int objectionWindowDays) =>
        resultsFirstPublishedAt?.AddDays(objectionWindowDays);

    public static bool IsObjectionWindowOpen(DateTime? resultsFirstPublishedAt, int objectionWindowDays, DateTime utcNow) =>
        ObjectionDeadlineUtc(resultsFirstPublishedAt, objectionWindowDays) is DateTime deadline && utcNow <= deadline;

    public static decimal EffectivePassMark(RecruitmentCompetition competition, CompetitionLine? line) =>
        line?.LinePassMark ?? competition.PassMark;
}
