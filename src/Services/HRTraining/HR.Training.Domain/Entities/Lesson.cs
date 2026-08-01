using System.ComponentModel.DataAnnotations;

using HRManagement.SharedKernel.Entities;



namespace HR.Training.Domain.Entities;



[Display(Name = "الدرس")]

public class Lesson : AuditableEntity

{

    [Display(Name = "وحدة الدورة")]

    public Guid CourseModuleId { get; set; }



    [Display(Name = "الترتيب")]

    public int Order { get; set; }



    [Required(ErrorMessage = "عنوان الدرس مطلوب")]

    [Display(Name = "عنوان الدرس")]

    [StringLength(200)]

    public string Title { get; set; } = string.Empty;



    [Display(Name = "المحتوى")]

    public string? Content { get; set; }



    [Display(Name = "معرف ملف المحتوى")]

    public Guid? ContentFileId { get; set; }



    [Display(Name = "رابط مورد خارجي")]

    [StringLength(500)]

    public string? ExternalResourceUrl { get; set; }



    public virtual CourseModule? CourseModule { get; set; }

}

