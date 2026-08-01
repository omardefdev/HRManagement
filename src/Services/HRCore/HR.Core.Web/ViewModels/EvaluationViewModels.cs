using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.ViewModels;
using HR.Core.Domain.Entities;

namespace HR.Core.Web.ViewModels;

public class EvaluationListViewModel
{
    public PaginatedList<PerformanceEvaluation> Evaluations { get; set; } = null!;
    public SearchFilterBase Filter { get; set; } = new();
    public Guid? EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public EvaluationCreateViewModel CreateModel { get; set; } = new();
    public bool ShowCreateModal { get; set; }
}

public class EvaluationCreateViewModel
{
    public Guid EmployeeId { get; set; }

    [Display(Name = "السنة")]
    [Required(ErrorMessage = "السنة مطلوبة")]
    public int Year { get; set; } = DateTime.Now.Year;

    [Display(Name = "الدرجة")]
    [Range(0, 100, ErrorMessage = "الدرجة يجب أن تكون بين 0 و 100")]
    public decimal Score { get; set; }

    [Display(Name = "ملخص التقييم")]
    public string? Summary { get; set; }

    public string? EmployeeName { get; set; }

    public string ReturnTo { get; set; } = "index";
}

public class EvaluationEditViewModel : EvaluationCreateViewModel
{
    public Guid Id { get; set; }

    [Display(Name = "الحالة")]
    [StringLength(20)]
    public string Status { get; set; } = "draft";
}
