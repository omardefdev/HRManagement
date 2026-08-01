using HRManagement.SharedKernel.ViewModels;
using HR.Core.Domain.Entities;

namespace HR.Core.Web.ViewModels;

public class EmployeeListViewModel
{
    public PaginatedList<Employee> Employees { get; set; } = null!;
    public EmployeeSearchFilter Filter { get; set; } = new();
}

public class EmployeeSearchFilter : SearchFilterBase
{
    public Guid? OrgUnitId { get; set; }
    public string? Status { get; set; }
    public string? Gender { get; set; }

    /// <summary>تصفية اختيارية من الواجهة (يُطبّق تقييد الدور فوقها).</summary>
    public string? Cadre { get; set; }
}
