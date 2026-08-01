using System.ComponentModel.DataAnnotations;

using HRManagement.SharedKernel.Entities;



namespace HR.Contracts.Domain.Entities;



[Display(Name = "القرار الإداري")]

public class AdministrativeDecision : AuditableEntity

{

    [Display(Name = "الموضوع")]

    [Required(ErrorMessage = "الموضوع مطلوب")]

    public string Subject { get; set; } = null!;



    [Display(Name = "رقم القرار")]

    [Required(ErrorMessage = "رقم القرار مطلوب")]

    public string DecisionNumber { get; set; } = null!;



    [Display(Name = "الجهة المصدرة")]

    public string? IssuedBy { get; set; }



    [Display(Name = "تاريخ الإصدار")]

    public DateOnly? IssuedDate { get; set; }



    [Display(Name = "الموظف المتعلق")]

    public Guid? RelatedEmployeeId { get; set; }



    public Guid? FileId { get; set; }

}

