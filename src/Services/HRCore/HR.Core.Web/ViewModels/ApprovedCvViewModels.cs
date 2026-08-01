using System.ComponentModel.DataAnnotations;



namespace HR.Core.Web.ViewModels;



public class ApprovedCvCreateViewModel

{

    public Guid EmployeeId { get; set; }

    public string? EmployeeName { get; set; }



    [Display(Name = "ملف السيرة")]

    public Guid? FileId { get; set; }



    [Display(Name = "ملاحظات")]

    public string? Notes { get; set; }



    [Display(Name = "المعتمد")]

    public Guid? ApprovedBy { get; set; }



    [Display(Name = "تاريخ الاعتماد")]

    [DataType(DataType.DateTime)]

    public DateTime? ApprovedAt { get; set; }

}



public class ApprovedCvEditViewModel : ApprovedCvCreateViewModel

{

    public Guid Id { get; set; }

}

