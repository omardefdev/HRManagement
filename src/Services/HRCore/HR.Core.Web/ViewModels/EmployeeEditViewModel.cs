using System.ComponentModel.DataAnnotations;

using HRManagement.SharedKernel.Enums;

using Microsoft.AspNetCore.Mvc.Rendering;



namespace HR.Core.Web.ViewModels;



public class EmployeeEditViewModel

{

    public Guid Id { get; set; }



    [Display(Name = "الرقم الوطني")]

    [Required(ErrorMessage = "الرقم الوطني مطلوب")]

    [StringLength(20)]

    public string NationalId { get; set; } = string.Empty;



    [Display(Name = "الاسم الأول")]

    [Required(ErrorMessage = "الاسم الأول مطلوب")]

    [StringLength(100)]

    public string FirstName { get; set; } = string.Empty;



    [Display(Name = "الاسم الأوسط")]

    [StringLength(100)]

    public string? MiddleName { get; set; }



    [Display(Name = "اسم العائلة")]

    [Required(ErrorMessage = "اسم العائلة مطلوب")]

    [StringLength(100)]

    public string LastName { get; set; } = string.Empty;



    [Display(Name = "تاريخ الميلاد")]

    [Required(ErrorMessage = "تاريخ الميلاد مطلوب")]

    [DataType(DataType.Date)]

    public DateTime BirthDate { get; set; }



    [Display(Name = "الجنس")]

    [Required(ErrorMessage = "الجنس مطلوب")]

    public Gender Gender { get; set; }



    [Display(Name = "الحالة الاجتماعية")]

    public MaritalStatus MaritalStatus { get; set; }



    [Display(Name = "الحالة الوظيفية")]

    public EmploymentStatus CurrentStatus { get; set; }



    [Display(Name = "الوحدة التنظيمية")]

    public Guid? PrimaryOrgUnitId { get; set; }



    [Display(Name = "تاريخ التعيين")]

    [DataType(DataType.Date)]

    public DateTime? HireDate { get; set; }



    [Display(Name = "صورة الموظف")]

    public Guid? PhotoFileId { get; set; }



    [Display(Name = "الشريحة الوظيفية")]

    public EmployeeCadre Cadre { get; set; }



    public SelectList? OrgUnits { get; set; }

}

