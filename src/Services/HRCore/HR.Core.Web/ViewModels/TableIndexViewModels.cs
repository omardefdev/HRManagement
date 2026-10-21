using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.SeparationOfDuties;

namespace HR.Core.Web.ViewModels;

public sealed class WorkforcePlansIndexViewModel
{
    public IReadOnlyList<WorkforcePlan> Items { get; init; } = [];
    public int? FiscalYear { get; init; }
    public Guid? OrgUnitId { get; init; }
    public string? Status { get; init; }
}

public sealed class HrStatisticReportsIndexViewModel
{
    public IReadOnlyList<HrStatisticReport> Items { get; init; } = [];
    public Guid? OrgUnitId { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public string? Q { get; init; }
}

public sealed class DailyAttendanceRecordsIndexViewModel
{
    public IReadOnlyList<DailyAttendanceRecord> Items { get; init; } = [];
    public Guid? EmployeeId { get; init; }
    public string? FilterEmployeeName { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public DailyAttendanceStatus? Status { get; init; }
}

public sealed class FieldInspectionVisitsIndexViewModel
{
    public IReadOnlyList<FieldInspectionVisit> Items { get; init; } = [];
    public string? Status { get; init; }
    public DateOnly? VisitFrom { get; init; }
    public DateOnly? VisitTo { get; init; }
    public string? Q { get; init; }
}

public sealed class DiwanCorrespondencesIndexViewModel
{
    public IReadOnlyList<DiwanCorrespondence> Items { get; init; } = [];
    public CorrespondenceDirection? Direction { get; init; }
    public string? Q { get; init; }
    public DateTime? RegisteredFrom { get; init; }
    public DateTime? RegisteredTo { get; init; }
    public Guid? RelatedEmployeeId { get; init; }
    public string? FilterEmployeeName { get; init; }
}

public sealed class DelegationsIndexViewModel
{
    public IReadOnlyList<ApprovalDelegation> Items { get; init; } = [];
    public Guid? PrincipalId { get; init; }
    public Guid? DelegateId { get; init; }
    public string? EntityTypeScope { get; init; }
    public bool? ActiveOnly { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}

public sealed class ShiftsIndexViewModel
{
    public IReadOnlyList<Shift> Items { get; init; } = [];
    public string? Q { get; init; }
    public ShiftType? ShiftType { get; init; }
    public bool? IsActive { get; init; }
}

public sealed class EmployeeCorrectionsIndexViewModel
{
    public IReadOnlyList<EmployeeCorrection> Items { get; init; } = [];
    public Guid? EmployeeId { get; init; }
    public string? FilterEmployeeName { get; init; }
    public ApprovalStatus? Status { get; init; }
    public bool? RequiresLegalApproval { get; init; }
    public DateTime? CreatedFrom { get; init; }
    public DateTime? CreatedTo { get; init; }
}

public sealed class KeyPositionsIndexViewModel
{
    public IReadOnlyList<KeyPosition> Items { get; init; } = [];
    public Guid? OrgUnitId { get; init; }
    public string? Q { get; init; }
    public int? MinCriticality { get; init; }
}

public sealed class RostersIndexViewModel
{
    public IReadOnlyList<ShiftRoster> Items { get; init; } = [];
    public Guid? OrgUnitId { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public string? Q { get; init; }
}

public sealed class CareerPathsIndexViewModel
{
    public IReadOnlyList<CareerPath> Items { get; init; } = [];
    public string? Q { get; init; }
}

public sealed class SuccessionPlansIndexViewModel
{
    public IReadOnlyList<SuccessionPlan> Items { get; init; } = [];
    public Guid? KeyPositionId { get; init; }
    public int? Year { get; init; }
}

public sealed class TalentMarketplaceIndexViewModel
{
    public IReadOnlyList<InternalVacancy> Items { get; init; } = [];
    public Guid? OrgUnitId { get; init; }
    public bool? IsOpen { get; init; }
    public DateOnly? ClosingFrom { get; init; }
    public DateOnly? ClosingTo { get; init; }
}

public sealed class PublicJobPostingsIndexViewModel
{
    public IReadOnlyList<PublicJobPosting> Items { get; init; } = [];
    public bool? IsPublished { get; init; }
    public string? Entity { get; init; }
    public string? Q { get; init; }
    public DateOnly? ClosingFrom { get; init; }
    public DateOnly? ClosingTo { get; init; }
}
