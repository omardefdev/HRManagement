using System.ComponentModel.DataAnnotations;
using HR.Movements.Domain.Entities;

namespace HR.Movements.Web.Models;

// PLAN-51 (P-8): the create and edit forms of HR.Movements bind these view models, never an entity. Each carries only the fields its form
// posts and its action uses, with the entity's validation; what the server owns (Id, TenantId, the audit stamps, Status, RequestedBy,
// IssuedBy, the approver, the cancellation, the source legal regime) cannot be set by a client. The views keep rendering the entity:
// ToEntity() rebuilds it for a form that fails validation.

public sealed class MovementNoticeForm
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "العنوان مطلوب")]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Display(Name = "تاريخ الإصدار")]
    public DateOnly IssueDate { get; set; }

    [Display(Name = "الفترة")]
    public string? Period { get; set; }

    [Display(Name = "البنود")]
    public string? Items { get; set; }

    public MovementNotice ToEntity() => new() { Title = Title, IssueDate = IssueDate, Period = Period, Items = Items };

    public MovementNotice ToEntity(Guid id)
    {
        var entity = ToEntity();
        entity.Id = id;
        return entity;
    }
}

public sealed class DistributionPlanForm
{
    public Guid Id { get; set; }

    [Required]
    [Display(Name = "الوحدة التنظيمية")]
    public Guid OrgUnitId { get; set; }

    [Required]
    [Display(Name = "تاريخ النفاذ")]
    public DateOnly EffectiveDate { get; set; }

    [Display(Name = "الأماكن المخطط لها")]
    public string? PlannedPositions { get; set; }

    [Display(Name = "طلب النقل المرتبط")]
    public Guid? TransferRequestId { get; set; }

    public DistributionPlan ToEntity() => new()
    {
        OrgUnitId = OrgUnitId,
        EffectiveDate = EffectiveDate,
        PlannedPositions = PlannedPositions,
        TransferRequestId = TransferRequestId
    };

    public DistributionPlan ToEntity(Guid id)
    {
        var entity = ToEntity();
        entity.Id = id;
        return entity;
    }
}

public sealed class SecondmentRequestForm
{
    public Guid Id { get; set; }

    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required(ErrorMessage = "الجهة المستضيفة مطلوبة")]
    [Display(Name = "الجهة المستضيفة")]
    public string HostOrg { get; set; } = string.Empty;

    [Required]
    [Display(Name = "تاريخ البداية")]
    public DateOnly StartDate { get; set; }

    [Required]
    [Display(Name = "تاريخ النهاية")]
    public DateOnly EndDate { get; set; }

    [Display(Name = "الشروط")]
    public string? Terms { get; set; }

    public SecondmentRequest ToEntity() => new()
    {
        EmployeeId = EmployeeId,
        HostOrg = HostOrg,
        StartDate = StartDate,
        EndDate = EndDate,
        Terms = Terms
    };

    public SecondmentRequest ToEntity(Guid id)
    {
        var entity = ToEntity();
        entity.Id = id;
        return entity;
    }
}

public sealed class TransferFinancialImpactForm
{
    [Required]
    [Display(Name = "طلب النقل")]
    public Guid TransferRequestId { get; set; }

    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "فرق الراتب")]
    public decimal SalaryChange { get; set; }

    [Display(Name = "فرق البدلات")]
    public decimal AllowanceChange { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "تاريخ الحساب")]
    public DateTime CalculatedAt { get; set; }

    public TransferFinancialImpact ToEntity() => new()
    {
        TransferRequestId = TransferRequestId,
        EmployeeId = EmployeeId,
        SalaryChange = SalaryChange,
        AllowanceChange = AllowanceChange,
        Notes = Notes,
        CalculatedAt = CalculatedAt
    };
}

public sealed class LoanAssignmentRequestForm
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "الوحدة المُعيرة")]
    public Guid LendingOrgUnitId { get; set; }

    [Required]
    [Display(Name = "الوحدة المستفيدة")]
    public Guid BorrowingOrgUnitId { get; set; }

    [Required]
    [Display(Name = "تاريخ البداية")]
    public DateOnly StartDate { get; set; }

    [Required]
    [Display(Name = "تاريخ النهاية")]
    public DateOnly EndDate { get; set; }

    [Display(Name = "الشروط")]
    public string? Terms { get; set; }

    public LoanAssignmentRequest ToEntity() => new()
    {
        EmployeeId = EmployeeId,
        LendingOrgUnitId = LendingOrgUnitId,
        BorrowingOrgUnitId = BorrowingOrgUnitId,
        StartDate = StartDate,
        EndDate = EndDate,
        Terms = Terms
    };
}

public sealed class PromotionRequestForm
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "من درجة")]
    public int FromGrade { get; set; }

    [Display(Name = "إلى درجة")]
    public int ToGrade { get; set; }

    [Display(Name = "من مرتبة")]
    public int FromStep { get; set; }

    [Display(Name = "إلى مرتبة")]
    public int ToStep { get; set; }

    [Required(ErrorMessage = "مرجع القرار مطلوب")]
    [Display(Name = "مرجع القرار")]
    [MaxLength(200)]
    public string TransitionDecisionRef { get; set; } = string.Empty;

    [Required]
    [Display(Name = "تاريخ النفاذ")]
    public DateOnly EffectiveDate { get; set; }

    public PromotionRequest ToEntity() => new()
    {
        EmployeeId = EmployeeId,
        FromGrade = FromGrade,
        ToGrade = ToGrade,
        FromStep = FromStep,
        ToStep = ToStep,
        TransitionDecisionRef = TransitionDecisionRef,
        EffectiveDate = EffectiveDate
    };
}

public sealed class DelegationRequestForm
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required(ErrorMessage = "الجهة المستضيفة مطلوبة")]
    [Display(Name = "الجهة المستضيفة")]
    [MaxLength(256)]
    public string HostOrg { get; set; } = string.Empty;

    [Required]
    [Display(Name = "تاريخ البداية")]
    public DateOnly StartDate { get; set; }

    [Required]
    [Display(Name = "تاريخ النهاية")]
    public DateOnly EndDate { get; set; }

    [Display(Name = "الشروط")]
    public string? Terms { get; set; }

    public DelegationRequest ToEntity() => new()
    {
        EmployeeId = EmployeeId,
        HostOrg = HostOrg,
        StartDate = StartDate,
        EndDate = EndDate,
        Terms = Terms
    };
}

public sealed class TransferRequestForm
{
    public Guid Id { get; set; }

    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "الوحدة المصدر")]
    public Guid FromOrgUnitId { get; set; }

    [Required]
    [Display(Name = "الوحدة المقصد")]
    public Guid ToOrgUnitId { get; set; }

    [Required]
    [Display(Name = "تاريخ الطلب")]
    public DateOnly RequestDate { get; set; }

    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    [Display(Name = "تاريخ النفاذ")]
    public DateOnly EffectiveDate { get; set; }

    [MaxLength(50)]
    public string? TargetLegalRegimeId { get; set; }

    [MaxLength(200)]
    public string? TransitionDecisionRef { get; set; }

    [MaxLength(100)]
    public string? ConditionCode { get; set; }

    [MaxLength(1000)]
    public string? ConflictNotes { get; set; }

    public TransferRequest ToEntity() => new()
    {
        EmployeeId = EmployeeId,
        FromOrgUnitId = FromOrgUnitId,
        ToOrgUnitId = ToOrgUnitId,
        RequestDate = RequestDate,
        Reason = Reason,
        EffectiveDate = EffectiveDate,
        TargetLegalRegimeId = TargetLegalRegimeId,
        TransitionDecisionRef = TransitionDecisionRef,
        ConditionCode = ConditionCode,
        ConflictNotes = ConflictNotes
    };

    public TransferRequest ToEntity(Guid id)
    {
        var entity = ToEntity();
        entity.Id = id;
        return entity;
    }
}
