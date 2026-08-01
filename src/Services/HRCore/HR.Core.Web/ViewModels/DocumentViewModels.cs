using System.ComponentModel.DataAnnotations;

using HRManagement.SharedKernel.Enums;



namespace HR.Core.Web.ViewModels;



public class DocumentCreateViewModel

{

    public Guid EmployeeId { get; set; }



    [Display(Name = "نوع المستند")]

    public DocumentType DocType { get; set; }



    [Display(Name = "عنوان المستند")]

    [Required(ErrorMessage = "عنوان المستند مطلوب")]

    [StringLength(200)]

    public string DocTitle { get; set; } = string.Empty;



    [Display(Name = "جهة الإصدار")]

    [StringLength(200)]

    public string? IssuedBy { get; set; }



    [Display(Name = "تاريخ الإصدار")]

    [DataType(DataType.Date)]

    public DateTime? IssueDate { get; set; }



    [Display(Name = "تاريخ الانتهاء")]

    [DataType(DataType.Date)]

    public DateTime? ExpiryDate { get; set; }



    [Display(Name = "ملف المستند")]

    public Guid? FileId { get; set; }



    public string? EmployeeName { get; set; }



    public string ReturnTo { get; set; } = "index";

}



public class DocumentEditViewModel : DocumentCreateViewModel

{

    public Guid Id { get; set; }

    public bool Verified { get; set; }

}

