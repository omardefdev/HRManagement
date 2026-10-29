using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;

namespace HR.Core.Web.ViewModels;

/// <summary>HCM-031 (PLAN-31): create/edit form of a competition. Every legal input comes from the approved reference (D-2).</summary>
public sealed class CompetitionFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "عنوان المسابقة مطلوب")]
    [StringLength(200)]
    [Display(Name = "عنوان المسابقة")]
    public string? Title { get; set; }

    [Required(ErrorMessage = "المرجع القانوني مطلوب")]
    [Display(Name = "المرجع القانوني")]
    public string? LegalSourceId { get; set; }

    [StringLength(2000)]
    [Display(Name = "شروط التقديم")]
    public string? ApplicationConditions { get; set; }

    [Display(Name = "قناة النشر")]
    public RecruitmentPublishingChannel PublishingChannel { get; set; } = RecruitmentPublishingChannel.PublicOnly;

    [DataType(DataType.Date)]
    [Display(Name = "تاريخ الإعلان")]
    public DateOnly? AnnouncementDate { get; set; }

    [Required(ErrorMessage = "تاريخ إغلاق التقديم مطلوب")]
    [DataType(DataType.Date)]
    [Display(Name = "تاريخ إغلاق التقديم")]
    public DateOnly? ApplicationClosingDate { get; set; }

    [Display(Name = "مدة الاعتراض (أيام)")]
    public int? ObjectionWindowDays { get; set; }

    [Display(Name = "صلاحية الاحتياط (أشهر)")]
    public int? ReserveValidityMonths { get; set; }

    [Display(Name = "الحد الأقصى للأسطر لكل متقدم")]
    public int? MaxLinesPerCandidate { get; set; }

    [Display(Name = "درجة النجاح (من 100)")]
    public decimal? PassMark { get; set; }

    [Display(Name = "عتبة التصحيح الثالث")]
    public decimal? ReconcileThreshold { get; set; }

    [Display(Name = "قاعدة التسوية")]
    public CompetitionReconcileRule? ReconcileRule { get; set; }

    /// <summary>Tie-break criteria in order (first = applied first).</summary>
    [Display(Name = "ترتيب كسر التعادل")]
    public List<CompetitionTieBreak?> TieBreakOrder { get; set; } = [];

    /// <summary>True when the legal inputs were pre-filled from <c>Recruitment:Competition:Defaults</c> (non-production only).</summary>
    public bool PrefilledFromDefaults { get; set; }

    public CompetitionInput ToInput() => new(
        Title,
        LegalSourceId,
        ApplicationConditions,
        PublishingChannel,
        AnnouncementDate,
        ApplicationClosingDate,
        new CompetitionLegalInputs(
            ObjectionWindowDays,
            ReserveValidityMonths,
            MaxLinesPerCandidate,
            PassMark,
            ReconcileThreshold,
            ReconcileRule,
            TieBreakOrder.Any(t => t is not null) ? TieBreakOrder.Where(t => t is not null).Select(t => t!.Value).ToList() : null));

    public static CompetitionFormViewModel From(RecruitmentCompetition competition) => new()
    {
        Id = competition.Id,
        Title = competition.Title,
        LegalSourceId = competition.LegalSourceId,
        ApplicationConditions = competition.ApplicationConditions,
        PublishingChannel = competition.PublishingChannel,
        AnnouncementDate = competition.AnnouncementDate,
        ApplicationClosingDate = competition.ApplicationClosingDate,
        ObjectionWindowDays = competition.ObjectionWindowDays,
        ReserveValidityMonths = competition.ReserveValidityMonths,
        MaxLinesPerCandidate = competition.MaxLinesPerCandidate,
        PassMark = competition.PassMark,
        ReconcileThreshold = competition.ReconcileThreshold,
        ReconcileRule = competition.ReconcileRule,
        TieBreakOrder = CompetitionWorkflowRules.ParseTieBreakOrder(competition.TieBreakOrder).Select(t => (CompetitionTieBreak?)t).ToList()
    };
}

/// <summary>HCM-031: add/edit form of a competition line.</summary>
public sealed class CompetitionLineFormViewModel
{
    public Guid CompetitionId { get; set; }
    public Guid? LineId { get; set; }

    [Required(ErrorMessage = "عنوان السطر مطلوب")]
    [StringLength(200)]
    [Display(Name = "عنوان السطر")]
    public string? Title { get; set; }

    [Required(ErrorMessage = "الدرجة مطلوبة")]
    [StringLength(20)]
    [Display(Name = "الدرجة")]
    public string? GradeCode { get; set; }

    [Display(Name = "الوظيفة في الدليل")]
    public Guid? JobCatalogEntryId { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "درجة نجاح السطر")]
    public decimal? LinePassMark { get; set; }

    public CompetitionLineInput ToInput() => new(Title, GradeCode, JobCatalogEntryId, OrgUnitId, LinePassMark);
}

public sealed record CompetitionIndexRow(
    Guid Id,
    string Number,
    string Title,
    RecruitmentCompetitionStatus Status,
    DateOnly ApplicationClosingDate,
    int Lines,
    int Vacancies);

public sealed class CompetitionIndexViewModel
{
    public IReadOnlyList<CompetitionIndexRow> Rows { get; init; } = [];
    public RecruitmentCompetitionStatus? Status { get; init; }
    public bool CanWrite { get; init; }
}

public sealed record CompetitionLineRequisitionRow(Guid Id, string RequisitionNumber, string JobTitle, RequisitionStatus Status, string? OrgUnitName);

public sealed record CompetitionLineRow(
    CompetitionLine Line,
    IReadOnlyList<CompetitionLineRequisitionRow> Requisitions,
    IReadOnlyList<AttachableRequisition> Attachable);

public sealed class CompetitionDetailsViewModel
{
    public required RecruitmentCompetition Competition { get; init; }
    public string LegalSourceLabel { get; init; } = "—";
    public IReadOnlyList<CompetitionLineRow> Lines { get; init; } = [];
    public IReadOnlyList<CompetitionTieBreak> TieBreakOrder { get; init; } = [];
    public required WorkflowPanelViewModel Workflow { get; init; }
    public bool CanWrite { get; init; }
    public bool CanApprove { get; init; }
    public int ApplicationCount { get; init; }

    public bool IsEditable => CompetitionWorkflowRules.CanEdit(Competition.Status);
    public int VacancyCount => Lines.Sum(l => l.Requisitions.Count);
}
