using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Health.Domain.Entities;

[Display(Name = "تدريب الإسعافات الأولية")]
public class FirstAidTrainingRecord : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Display(Name = "اسم الموظف")]
    [StringLength(300)]
    public string? EmployeeName { get; set; }

    [Required]
    [Display(Name = "تاريخ التدريب")]
    public DateOnly TrainingDate { get; set; }

    [Display(Name = "الجهة المدرِّبة")]
    [StringLength(200)]
    public string? Provider { get; set; }

    [Display(Name = "رقم الشهادة")]
    [StringLength(100)]
    public string? CertificateNo { get; set; }

    [Display(Name = "تاريخ انتهاء الصلاحية")]
    public DateOnly? ExpiryDate { get; set; }
}
