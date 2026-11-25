using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-56 (G-PAY-04): what a rejected version of a run was, kept when the rejection reopens the run and its lines are deleted for
/// the recalculation: the version's number, how many lines it had, their total, a fingerprint of them, who rejected it and why.
/// One row per rejected version of a run.
/// </summary>
[Display(Name = "نسخة مرفوضة من تشغيل الرواتب")]
public class PayrollRunRevision : AuditableEntity
{
    public const int NoteMaxLength = 2000;

    [Display(Name = "تشغيل الرواتب")]
    public Guid PayrollRunId { get; set; }

    /// <summary>The number of the version that was rejected (the run then carries this number plus one).</summary>
    [Display(Name = "رقم النسخة")]
    public int Revision { get; set; }

    [Display(Name = "عدد البنود")]
    public int LineCount { get; set; }

    [Display(Name = "إجمالي صافي الرواتب")]
    public decimal TotalNetPay { get; set; }

    /// <summary>SHA-256 over the version's lines and their components in a fixed order: two versions with the same lines have the same fingerprint.</summary>
    [MaxLength(64)]
    [Display(Name = "بصمة البنود")]
    public string LinesFingerprint { get; set; } = "";

    [Display(Name = "الرافض")]
    public Guid RejectedBy { get; set; }

    [Display(Name = "تاريخ الرفض")]
    public DateTime RejectedAt { get; set; }

    [MaxLength(NoteMaxLength)]
    [Display(Name = "ملاحظة الرفض")]
    public string Note { get; set; } = "";

    public PayrollRun PayrollRun { get; set; } = null!;
}
