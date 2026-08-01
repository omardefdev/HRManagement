using System.ComponentModel.DataAnnotations;

using HRManagement.SharedKernel.Entities;



namespace IDCard.Domain.Entities;



[Display(Name = "ختم إداري")]

public class OfficialSealRecord : AuditableEntity

{

    [Required(ErrorMessage = "اسم الختم مطلوب")]

    [Display(Name = "الاسم")]

    public string Name { get; set; } = null!;



    [Display(Name = "معرف ملف الختم")]

    public Guid? SealFileId { get; set; }



    [Display(Name = "صادر عن")]

    public string? IssuedBy { get; set; }



    [Display(Name = "ساري من")]

    public DateOnly? ValidFrom { get; set; }



    [Display(Name = "ساري إلى")]

    public DateOnly? ValidTo { get; set; }

}

