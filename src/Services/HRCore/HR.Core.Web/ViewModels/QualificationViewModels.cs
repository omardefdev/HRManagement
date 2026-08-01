using System.ComponentModel.DataAnnotations;



namespace HR.Core.Web.ViewModels;



public class QualificationCreateViewModel

{

    public Guid EmployeeId { get; set; }



    [Display(Name = "كود التأهيل المهني")]

    [StringLength(50)]

    public string? QualificationCode { get; set; }



    [Display(Name = "اسم المؤهل")]

    [Required(ErrorMessage = "اسم المؤهل مطلوب")]

    [StringLength(300)]

    public string Title { get; set; } = string.Empty;



    [Display(Name = "الجهة المانحة")]

    [StringLength(300)]

    public string? Provider { get; set; }



    [Display(Name = "تاريخ الإتمام")]

    [DataType(DataType.Date)]

    public DateTime? CompletionDate { get; set; }



    [Display(Name = "ملف الشهادة")]

    public Guid? FileId { get; set; }



    public string? EmployeeName { get; set; }



    public string ReturnTo { get; set; } = "index";

}



public class QualificationEditViewModel : QualificationCreateViewModel

{

    public Guid Id { get; set; }

}

