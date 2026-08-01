using System.ComponentModel.DataAnnotations;

using HRManagement.SharedKernel.Entities;



namespace HR.Core.Domain.Entities;



public class ApprovedCV : AuditableEntity

{

    [Display(Name = "الموظف")]

    [Required]

    public Guid EmployeeId { get; set; }



    [Display(Name = "معرف الملف")]

    public Guid? FileId { get; set; }



    [Display(Name = "ملاحظات")]

    public string? Notes { get; set; }



    [Display(Name = "المعتمد")]

    public Guid? ApprovedBy { get; set; }



    [Display(Name = "تاريخ الاعتماد")]

    public DateTime? ApprovedAt { get; set; }



    public virtual Employee? Employee { get; set; }

}

