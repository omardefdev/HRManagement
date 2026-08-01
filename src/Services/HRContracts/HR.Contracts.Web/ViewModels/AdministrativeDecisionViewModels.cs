using System.ComponentModel.DataAnnotations;



namespace HR.Contracts.Web.ViewModels;



public class AdministrativeDecisionViewModel

{

    public Guid? Id { get; set; }



    [Display(Name = "الموضوع")]

    [Required]

    [StringLength(500)]

    public string Subject { get; set; } = string.Empty;



    [Display(Name = "رقم القرار")]

    [Required]

    [StringLength(100)]

    public string DecisionNumber { get; set; } = string.Empty;



    [Display(Name = "الجهة المصدرة")]

    public string? IssuedBy { get; set; }



    [Display(Name = "تاريخ الإصدار")]

    [DataType(DataType.Date)]

    public DateOnly? IssuedDate { get; set; }



    [Display(Name = "الموظف المتعلق")]

    public Guid? RelatedEmployeeId { get; set; }



    [Display(Name = "ملف القرار")]

    public Guid? FileId { get; set; }

}

