using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Auth.Directory.Domain.Entities;

[Display(Name = "سجل مزامنة الدليل")]
public class DirectorySyncRecord : BaseEntity
{
    [Display(Name = "الخدمة المصدرية")]
    public string? SourceService { get; set; }

    [Display(Name = "السجلات المعالجة")]
    public int RecordsProcessed { get; set; }

    [Display(Name = "عدد النجاح")]
    public int SuccessCount { get; set; }

    [Display(Name = "عدد الفشل")]
    public int FailureCount { get; set; }

    [Display(Name = "وقت البدء")]
    public DateTime StartedAt { get; set; }

    [Display(Name = "وقت الاكتمال")]
    public DateTime? CompletedAt { get; set; }

    [Display(Name = "السجل")]
    public string? Log { get; set; }
}
