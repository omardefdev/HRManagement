using System.ComponentModel.DataAnnotations;

namespace HR.Core.Web.ViewModels;

/// <summary>HCM-030 (H2): <c>/careers/my/login</c> — reference number + national id (posted, never in the URL).</summary>
public sealed class CandidatePortalLoginViewModel
{
    [Display(Name = "الرقم المرجعي")]
    [StringLength(40)]
    public string? ReferenceNumber { get; set; }

    [Display(Name = "الرقم الوطني")]
    [StringLength(20)]
    public string? NationalId { get; set; }
}

/// <summary>
/// HCM-030 (H2): code entry. The reference number travels in a hidden field (the gateway does not forward the TempData
/// cookie, and the national id is not repeated).
/// </summary>
public sealed class CandidatePortalCodeViewModel
{
    [StringLength(40)]
    public string? ReferenceNumber { get; set; }

    [Display(Name = "رمز التحقق")]
    [StringLength(20)]
    public string? Code { get; set; }

    /// <summary>The neutral message shown after a code request.</summary>
    public string? Notice { get; set; }
}

/// <summary>
/// HCM-030 (PLAN-30 I): «طلبي» — everything the signed-in candidate may see about the application of the session. Built by
/// <see cref="HR.Core.Web.Services.CandidatePortalService"/>; carries no ids the forms could post back.
/// </summary>
public sealed class CandidatePortalPageViewModel
{
    public required string ReferenceNumber { get; init; }
    public string? JobTitle { get; init; }
    public DateTime SubmittedAtUtc { get; init; }
    public DateTime SessionExpiresAtUtc { get; init; }

    /// <summary>Linked (unified-intake) application; legacy citizen rows have none and show only their status.</summary>
    public HR.Core.Domain.Services.CandidatePublicView? Status { get; init; }
    public string? LegacyStatusLabel { get; init; }

    public IReadOnlyList<CandidatePortalInterviewRow> Interviews { get; init; } = [];
    public IReadOnlyList<CandidatePortalDocumentRow> Documents { get; init; } = [];
    public IReadOnlyList<(string Code, string LabelAr)> MissingDocumentTypes { get; init; } = [];
    public bool CanUploadDocuments { get; init; }
    public string? DocumentsClosedReason { get; init; }
    public long MaxDocumentBytes { get; init; }
    public long MaxTotalDocumentBytes { get; init; }

    public CandidatePortalOfferCard? Offer { get; init; }
    public CandidatePortalAppealCard? Appeal { get; init; }
    /// <summary>HCM-031 (PLAN-31): set for a competition application.</summary>
    public CandidatePortalCompetitionCard? Competition { get; init; }

    public bool CanWithdraw { get; init; }

    /// <summary>The offer is accepted: no portal withdrawal (DECISIONS I), the candidate is told whom to contact.</summary>
    public bool WithdrawBlockedByAcceptance { get; init; }

    /// <summary>Second step of the withdrawal (in-page confirm panel, <c>?step=withdraw</c>).</summary>
    public bool ShowWithdrawConfirm { get; set; }

    /// <summary>Success message after a post-redirect-get (<c>?done=…</c>); never carries data from the request.</summary>
    public string? Notice { get; set; }

    /// <summary>Values typed in a refused form, shown again (never the national id).</summary>
    public CandidatePortalPostedValues Posted { get; set; } = new();
}

/// <summary>HCM-030 (I): an interview as the candidate sees it — date, time, place or «عن بُعد»; no panel, no notes.</summary>
public sealed record CandidatePortalInterviewRow(string DateText, string TimeText, string PlaceText, string StatusLabel, bool Upcoming);

/// <summary>HCM-030 (I): one uploaded document (type, name, size, date, scan state); no download on the portal.</summary>
public sealed record CandidatePortalDocumentRow(string TypeLabel, string FileName, string SizeText, string UploadedOnText, string ScanLabel);

/// <summary>
/// HCM-030 (I): the active offer (Issued or Accepted) with its terms — shown only after the one-time code. Dates are
/// calendar dates of the offer letter (<see cref="HR.Core.Infrastructure.Services.Recruitment.OfferLetterDocumentData"/>).
/// </summary>
public sealed record CandidatePortalOfferCard(
    string OfferNumber,
    string PositionTitle,
    string? GradeCode,
    string SalaryText,
    string StartDateText,
    string? ExpiresOnText,
    string? VerificationCode,
    bool IsAccepted,
    bool IsExpired,
    bool CanRespond,
    bool CanDownload);

public enum CandidatePortalAppealState
{
    /// <summary>Rejected, appealable, window open, no appeal yet.</summary>
    CanFile,
    /// <summary>The window has passed: no button, the closing date is shown.</summary>
    WindowClosed,
    Pending,
    /// <summary>Pending, the reviewer asked for information the candidate has not supplied yet.</summary>
    AwaitingInfo,
    Decided
}

public sealed record CandidatePortalAppealCard(CandidatePortalAppealState State, string? DateText, string? InfoRequest);

/// <summary>HCM-030 (I): typed values echoed back after a refused post.</summary>
/// <summary>HCM-031 (PLAN-31): the competition part of «طلبي».</summary>
public sealed record CandidatePortalCompetitionCard(
    string Number,
    string Title,
    IReadOnlyList<string> Preferences,
    IReadOnlyList<CandidatePortalExamRow> Exams,
    CandidatePortalCompetitionResult? Result,
    bool CanObject,
    string? ObjectionDeadline,
    string? ObjectionClosedText,
    string? ObjectionStatusText,
    IReadOnlyList<(string Type, string Label)> ObjectionPapers,
    string? AllocationText,
    IReadOnlyList<string> ReserveTexts);

/// <summary>HCM-031: one exam the candidate is seated for (the card is downloaded by exam type — no id in the URL).</summary>
public sealed record CandidatePortalExamRow(string Type, string TypeLabel, string Title, string When, string Venue, int Seat, string ExamNumber, bool CardIssued);

/// <summary>HCM-031: the candidate's own result — shown only once the results are published.</summary>
public sealed record CandidatePortalCompetitionResult(string? FinalScore, int? Rank, string OutcomeLabel, string PublishedOn);

public sealed class CandidatePortalPostedValues
{
    public string? AcceptFullName { get; set; }
    public string? DeclineReason { get; set; }
    public string? WithdrawReason { get; set; }
    public string? AppealReason { get; set; }
    /// <summary>HCM-031: the typed result objection.</summary>
    public string? ObjectionReason { get; set; }
    public string? AppealInfo { get; set; }
}

/// <summary>HCM-030 (I): accept — checkbox «أقرّ بأنني اطلعت على شروط العرض وأقبلها» + the full name typed to match.</summary>
public sealed class CandidatePortalAcceptForm
{
    public bool Acknowledge { get; set; }

    [StringLength(200)]
    public string? FullName { get; set; }
}

/// <summary>HCM-030 (I): decline reason, appeal reason or requested information (one text field).</summary>
public sealed class CandidatePortalTextForm
{
    [StringLength(4000)]
    public string? Text { get; set; }
}

/// <summary>HCM-030 (I): withdrawal — reason + the confirmation of the in-page panel.</summary>
public sealed class CandidatePortalWithdrawForm
{
    [StringLength(500)]
    public string? Reason { get; set; }

    public bool Confirm { get; set; }
}

/// <summary>
/// HCM-030 (I): missing documents uploaded from «طلبي». Same fields, document types and limits as the apply form
/// (<see cref="CitizenApplicationApplyViewModel"/>).
/// </summary>
public sealed class CandidatePortalDocumentsForm
{
    public Microsoft.AspNetCore.Http.IFormFile? CvFile { get; set; }
    public List<Microsoft.AspNetCore.Http.IFormFile>? CertificateFiles { get; set; }
    public Microsoft.AspNetCore.Http.IFormFile? IdCopyFile { get; set; }
    public List<Microsoft.AspNetCore.Http.IFormFile>? OtherFiles { get; set; }

    public IReadOnlyList<(string DocumentType, Microsoft.AspNetCore.Http.IFormFile File, string FieldName)> AttachedFiles()
    {
        var files = new List<(string, Microsoft.AspNetCore.Http.IFormFile, string)>();
        if (CvFile is not null)
            files.Add((HR.Core.Domain.Services.CandidateDocumentTypes.Cv, CvFile, nameof(CvFile)));
        files.AddRange((CertificateFiles ?? []).Where(f => f is not null)
            .Select(f => (HR.Core.Domain.Services.CandidateDocumentTypes.Certificate, f, nameof(CertificateFiles))));
        if (IdCopyFile is not null)
            files.Add((HR.Core.Domain.Services.CandidateDocumentTypes.IdCopy, IdCopyFile, nameof(IdCopyFile)));
        files.AddRange((OtherFiles ?? []).Where(f => f is not null)
            .Select(f => (HR.Core.Domain.Services.CandidateDocumentTypes.Other, f, nameof(OtherFiles))));
        return files;
    }
}
