using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>
/// HCM-030 (G3): one candidate retention run for one tenant — the weekly job writes one row per tenant it processed, a
/// run started from <c>CandidateLifecycle/Index</c> one row for the admin's tenant. Counts only, never an applicant field.
/// </summary>
public class CandidateRetentionRun : BaseEntity
{
    public const string JobTrigger = "Job";

    [Display(Name = "بدأ في")]
    public DateTime StartedAt { get; set; }

    /// <summary>Null when the run did not get through its rows (cancelled or a run-level failure; see <see cref="Error"/>).</summary>
    [Display(Name = "انتهى في")]
    public DateTime? FinishedAt { get; set; }

    /// <summary>True when <c>Recruitment:Retention:Apply</c> was false or a dry run was asked for: counts only.</summary>
    [Display(Name = "تشغيل تجريبي")]
    public bool DryRun { get; set; }

    /// <summary>Pairs (and legacy citizen rows) due for anonymization: retention elapsed, no legal hold, no pending appeal.</summary>
    [Display(Name = "المستحق")]
    public int DueCount { get; set; }

    [Display(Name = "أُخفيت هويته")]
    public int AnonymizedCount { get; set; }

    [Display(Name = "مستثنى (حجز قانوني)")]
    public int SkippedLegalHold { get; set; }

    [Display(Name = "مستثنى (استئناف معلّق)")]
    public int SkippedAppeal { get; set; }

    /// <summary><see cref="JobTrigger"/> or the id of the admin who started the run.</summary>
    [Required, StringLength(64)]
    [Display(Name = "بدأه")]
    public string TriggeredBy { get; set; } = JobTrigger;

    /// <summary>Sanitized failure summary (counts and exception type names, never row data).</summary>
    [StringLength(1000)]
    [Display(Name = "خطأ")]
    public string? Error { get; set; }
}
