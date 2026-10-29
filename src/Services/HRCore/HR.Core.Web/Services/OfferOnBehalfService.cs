using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Web.Services;

/// <summary>HCM-030 (J): which evidence file of an offer is downloaded.</summary>
public enum OfferEvidenceKind
{
    Acceptance,
    Decline
}

/// <summary>
/// HCM-030 (PLAN-30 J): HR records a paper response on the candidate's behalf (OfferLetters/Details).
/// <list type="bullet">
/// <item>Acceptance («قبول نيابةً عن المرشح»): a reason of at least
/// <see cref="OfferWorkflowRules.MinOnBehalfAcceptanceReasonLength"/> characters and the signed paper acceptance are
/// mandatory; audited <c>AcceptOfferOnBehalf</c> with the reason and the evidence file id.</item>
/// <item>Decline: the evidence file is optional; the reason rules are <see cref="RecruitmentOfferService.DeclineAsync"/>'s.</item>
/// </list>
/// The evidence is relayed server-side through HR.Core's service identity (<see cref="ICandidateDocumentUploadClient"/>,
/// File.Storage profile <c>CandidateDocument</c>: pdf/jpg/png up to 5 MB, scanned) — the browser upload widget needs
/// <c>ModuleFileStorage</c>, which HR users do not hold (DECISIONS J). The file reference
/// (<c>hr-core:OfferLetter:{id:N}:AcceptanceEvidence</c> / <c>…:DeclineEvidence</c>) is staged in the outbox with the
/// offer's tenant and saved in the same transaction as the response; a refused response saves nothing.
/// </summary>
public sealed class OfferOnBehalfService(
    HRCoreDbContext db,
    RecruitmentOfferService offers,
    ICandidateDocumentUploadClient files,
    IFileReferenceOutbox fileReferenceOutbox,
    IOptions<RecruitmentOptions> options)
{
    public const string AcceptanceEvidenceField = "AcceptanceEvidence";
    public const string DeclineEvidenceField = "DeclineEvidence";

    public static readonly string ReasonRequiredMessage =
        $"سبب القبول نيابةً عن المرشح مطلوب ({OfferWorkflowRules.MinOnBehalfAcceptanceReasonLength} حرفاً على الأقل).";
    public const string ReasonTooLongMessage = "سبب القبول يجب ألا يتجاوز 1000 حرف.";
    public const string EvidenceRequiredMessage =
        "مستند إثبات القبول مطلوب: نسخة موقّعة من قبول المرشح الورقي (PDF أو JPG أو PNG حتى 5 ميغابايت).";
    public const string EvidenceUploadFailedMessage =
        "تعذّر رفع مستند الإثبات حالياً، يرجى المحاولة لاحقاً. لم يُسجَّل أي تغيير على العرض.";
    public const string OfferNotFoundMessage = "خطاب العرض غير موجود.";

    public async Task<(bool Success, string? Error)> AcceptAsync(
        Guid offerId,
        string? reason,
        IFormFile? evidence,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var trimmed = reason?.Trim();
        if (trimmed is null || trimmed.Length < OfferWorkflowRules.MinOnBehalfAcceptanceReasonLength)
            return (false, ReasonRequiredMessage);
        if (trimmed.Length > 1000)
            return (false, ReasonTooLongMessage);
        if (evidence is null || evidence.Length == 0)
            return (false, EvidenceRequiredMessage);

        // State first, so a refused acceptance (expired, wrong status) relays no file.
        var offer = await LoadAsync(offerId, ct);
        if (offer is null)
            return (false, OfferNotFoundMessage);
        if (RecruitmentOfferService.GetAcceptError(offer) is string stateError)
            return (false, stateError);

        var (fileId, uploadError) = await UploadAndStageAsync(offer, evidence, AcceptanceEvidenceField, ct);
        if (fileId is null)
            return (false, uploadError);

        var result = await offers.AcceptAsync(offerId, OfferAcceptedBy.HrOnBehalf, trimmed, fileId, actorUserId, ct);
        if (!result.Success)
            UnstageReference(fileId.Value);
        return result;
    }

    public async Task<OfferSettlementResult> DeclineAsync(
        Guid offerId,
        string? reason,
        IFormFile? evidence,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        Guid? fileId = null;
        if (evidence is { Length: > 0 })
        {
            // Same checks as DeclineAsync before relaying the optional file, so a refused decline stores nothing.
            var offer = await LoadAsync(offerId, ct);
            if (offer is null)
                return new OfferSettlementResult(false, OfferNotFoundMessage, null, null);
            if (RecruitmentOfferService.GetDeclineError(offer, reason, OfferDeclinedBy.HrOnBehalf) is string declineError)
                return new OfferSettlementResult(false, declineError, null, null);

            var (uploaded, uploadError) = await UploadAndStageAsync(offer, evidence, DeclineEvidenceField, ct);
            if (uploaded is null)
                return new OfferSettlementResult(false, uploadError, null, null);
            fileId = uploaded;
        }

        var result = await offers.DeclineAndDraftNextAsync(offerId, reason, OfferDeclinedBy.HrOnBehalf, actorUserId, fileId, ct);
        if (!result.Success && fileId is Guid staged)
            UnstageReference(staged);
        return result;
    }

    /// <summary>
    /// The bytes of an offer's evidence file through HR.Core's service identity (HR users lack <c>ModuleFileStorage</c>).
    /// Only Clean files; other tenants' offers are not found.
    /// </summary>
    public async Task<(OfferLetter? Offer, CandidateFileContentResult Result)> OpenEvidenceAsync(
        Guid offerId,
        OfferEvidenceKind kind,
        CancellationToken ct = default)
    {
        var offer = await db.OfferLetters.AsNoTracking().FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null || (!db.BypassTenantFilter && offer.TenantId != db.EffectiveTenantId))
            return (null, new CandidateFileContentResult.NotFound());
        var fileId = kind == OfferEvidenceKind.Acceptance ? offer.AcceptanceEvidenceFileId : offer.DeclineEvidenceFileId;
        if (fileId is not Guid id)
            return (offer, new CandidateFileContentResult.NotFound());

        var metadata = await files.GetMetadataAsync(offer.TenantId, [id], ct);
        if (metadata is null)
            return (offer, new CandidateFileContentResult.Unavailable("Metadata unavailable"));
        if (!metadata.TryGetValue(id, out var meta) || !CandidateDocumentScanStatus.CanDownload(meta.ScanStatus))
            return (offer, new CandidateFileContentResult.NotFound());

        return (offer, await files.GetContentAsync(offer.TenantId, id, ct));
    }

    public static string HolderKey(Guid offerId, string field) =>
        FileReferenceClientExtensions.BuildHolderKey(HrFileServices.HrCore, nameof(OfferLetter), offerId, field);

    private Task<OfferLetter?> LoadAsync(Guid offerId, CancellationToken ct) =>
        db.OfferLetters.AsNoTracking()
            .Include(o => o.RecruitmentApplication)
            .FirstOrDefaultAsync(o => o.Id == offerId, ct);

    /// <summary>Local checks, relay to File.Storage, then the staged (unsaved) reference registration in the offer's tenant.</summary>
    private async Task<(Guid? FileId, string? Error)> UploadAndStageAsync(
        OfferLetter offer,
        IFormFile evidence,
        string field,
        CancellationToken ct)
    {
        var outcome = await CandidateDocumentFileValidator.PrepareAsync(
            [new CandidateDocumentFileValidator.Input(field, evidence, field)],
            options.Value.CandidateDocuments,
            ct);
        if (!outcome.IsValid)
            return (null, "مستند الإثبات: " + outcome.Errors[0].Error);

        if (await files.UploadAsync(offer.TenantId, outcome.Files[0], ct) is not CandidateDocumentUploadResult.Uploaded upload)
            return (null, EvidenceUploadFailedMessage);

        fileReferenceOutbox.EnqueueRegister(upload.FileId, HolderKey(offer.Id, field));
        // Explicit tenant (DECISIONS J), as CandidateDocumentService.Stage: the row belongs to the offer's tenant.
        foreach (var entry in OutboxEntries(upload.FileId))
            entry.Entity.TenantId = offer.TenantId;
        return (upload.FileId, null);
    }

    /// <summary>The response was refused after the relay: the staged reference is dropped (the file stays unreferenced).</summary>
    private void UnstageReference(Guid fileId)
    {
        foreach (var entry in OutboxEntries(fileId))
            entry.State = EntityState.Detached;
    }

    private List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<FileReferenceOutboxMessage>> OutboxEntries(Guid fileId) =>
        db.ChangeTracker.Entries<FileReferenceOutboxMessage>()
            .Where(e => e.State == EntityState.Added && e.Entity.FileId == fileId)
            .ToList();
}
