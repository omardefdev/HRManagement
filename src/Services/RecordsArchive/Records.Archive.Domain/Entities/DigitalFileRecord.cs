using System.ComponentModel.DataAnnotations;

using HRManagement.SharedKernel.Entities;



namespace Records.Archive.Domain.Entities;



[Display(Name = "ملف رقمي")]

public class DigitalFileRecord : AuditableEntity

{

    [Required]

    [Display(Name = "عنصر الأرشيف")]

    public Guid ArchiveItemId { get; set; }



    [Display(Name = "معرف الملف")]

    public Guid? FileId { get; set; }



    [Display(Name = "حجم الملف")]

    public long? FileSizeBytes { get; set; }



    [Display(Name = "المجموع الاختباري")]

    public string? Checksum { get; set; }



    public ArchiveItem ArchiveItem { get; set; } = default!;

}

