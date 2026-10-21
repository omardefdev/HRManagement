using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Establishment;

namespace HR.Core.Web.ViewModels;

public sealed class WorkforcePlanLineEditViewModel
{
    public Guid PlanId { get; set; }

    public string? GradeCode { get; set; }

    public Guid? JobCatalogEntryId { get; set; }

    public int PlannedCount { get; set; } = 1;

    public decimal PlannedFTE { get; set; } = 1m;

    public string? Notes { get; set; }
}

public sealed class WorkforcePlanApproveViewModel
{
    public Guid PlanId { get; set; }

    public string? LegalSourceId { get; set; }
}

public sealed class WorkforcePlanDetailsViewModel
{
    public HR.Core.Domain.Entities.WorkforcePlan Plan { get; init; } = null!;

    public EstablishmentGapSummary? GapSummary { get; init; }

    public DateTime? AsOf { get; init; }

    public bool CanEdit { get; init; }

    public WorkforcePlanLineEditViewModel NewLine { get; init; } = new();

    public WorkforcePlanApproveViewModel Approve { get; init; } = new();
}

public sealed class EstablishmentGapReportViewModel
{
    public Guid? OrgUnitId { get; init; }

    public int FiscalYear { get; init; }

    public DateTime? AsOf { get; init; }

    public Guid? WorkforcePlanId { get; init; }

    public EstablishmentGapSummary? Summary { get; init; }

    public string? OrgUnitName { get; init; }
}

public sealed class FinanceImportViewModel
{
    public int FiscalYear { get; set; } = DateTime.UtcNow.Year;

    public IFormFile? File { get; set; }
}

public sealed class FinanceReconciliationViewModel
{
    public int FiscalYear { get; init; }

    public Guid? BatchId { get; init; }

    public Guid? OrgUnitId { get; init; }

    public EstablishmentFinanceReconciliationResult? Result { get; init; }
}
