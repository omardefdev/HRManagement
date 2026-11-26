using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-58 (§2, P-6, G-PAY-08, D-PAY-9): the bank file of one sending of a batch — built from the orders submitted in that sending, to
/// their pinned accounts, with a closing control line (count and total) — stored as it was handed over and never regenerated. A batch has
/// one file per sending: <see cref="PaymentBatchFileKind.Initial"/> (<c>process</c>), <see cref="PaymentBatchFileKind.HeldRelease"/>
/// (<c>submit-held</c>), and a <see cref="PaymentBatchFileKind.Reissue"/> that two people approved (D-PAY-19), which supersedes the file
/// before it. The download reads <see cref="Content"/>; nothing builds a file again.
/// </summary>
[Display(Name = "ملف الصرف المصرفي")]
public class PaymentBatchFile : BaseEntity
{
    public const int ReasonMaxLength = 2000;

    [Display(Name = "دفعة الصرف")]
    public Guid PaymentBatchId { get; set; }

    /// <summary>1 for the batch's first file; one more for each later sending or reissue. Unique with the batch.</summary>
    [Display(Name = "رقم التسلسل")]
    public int Sequence { get; set; }

    [Display(Name = "نوع الملف")]
    public PaymentBatchFileKind Kind { get; set; }

    [Display(Name = "عدد الأوامر")]
    public int ControlCount { get; set; }

    [Display(Name = "مجموع الأوامر")]
    public decimal ControlTotal { get; set; }

    [MaxLength(64)]
    [Display(Name = "البصمة")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>The file's bytes as handed over (UTF-8). It names the beneficiaries' accounts: it is read by the download only.</summary>
    public byte[] Content { get; set; } = [];

    [Display(Name = "أنشأه")]
    public Guid GeneratedBy { get; set; }

    [Display(Name = "تاريخ الإنشاء")]
    public DateTime GeneratedAt { get; set; }

    /// <summary>The reissue that replaced this file (D-PAY-19): a superseded file is not to be sent again.</summary>
    [Display(Name = "أُبطل بالملف")]
    public Guid? SupersededById { get; set; }

    [MaxLength(ReasonMaxLength)]
    [Display(Name = "سبب إعادة الإصدار")]
    public string? ReissueReason { get; set; }

    [Display(Name = "اعتمد إعادة الإصدار")]
    public Guid? ReissueApprovedBy { get; set; }

    /// <summary>The download's file name: the batch's reference, the sequence and the kind; never an account.</summary>
    public string FileName => FormattableString.Invariant($"payment-batch-{PaymentBatchId:N}-{Sequence:D3}-{Kind.ToString().ToLowerInvariant()}.csv");
}
