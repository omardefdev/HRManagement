using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>HCM-030 (D1): an active MDM qualification offered on the apply form.</summary>
public sealed record QualificationOption(string Code, string NameAr);

/// <summary>
/// HCM-030 (A1, G-16): one intake path. Every public or internal application on a requisition is a
/// <see cref="RecruitmentApplication"/> from the moment it is submitted; the channel row
/// (<see cref="CitizenApplication"/> / <see cref="InternalApplication"/>) keeps the submitted data and links to it
/// through its existing <c>PromotedRecruitmentApplicationId</c>. New applications carry a profile snapshot and their
/// eligibility flags (D1/D4).
/// </summary>
public sealed class UnifiedIntakeService(HRCoreDbContext db, IOptions<RecruitmentOptions> options)
{
    public const string ManagedByUnifiedPathMessage = "يُدار هذا الطلب من مسار التوظيف الموحد.";

    /// <summary>Stages (no save) the recruitment application of a new public application and links the citizen row.</summary>
    /// <param name="providedDocumentTypes">
    /// HCM-030 (E4): types of the documents attached in the same post (none when null); a missing required type is
    /// flagged <c>MissingRequiredDocuments</c>.
    /// </param>
    public RecruitmentApplication CreateFromCitizen(
        CitizenApplication citizen,
        RecruitmentRequisition requisition,
        IEnumerable<string>? providedDocumentTypes = null)
    {
        var app = new RecruitmentApplication
        {
            // Public portal: no ambient tenant, so the tenant is set explicitly from the posting's row.
            TenantId = citizen.TenantId,
            ApplicantFullName = citizen.FullName,
            NationalId = citizen.NationalId,
            NationalIdHash = citizen.NationalIdHash ?? PiiMask.Sha256Hex(citizen.NationalId.Trim()),
            RequisitionId = requisition.Id,
            AppliedPosition = RecruitmentApplicationIntakeRules.DenormalizeJobTitle(requisition),
            AppliedAt = citizen.SubmittedAt,
            Status = RecruitmentApplicationStatus.Submitted,
            IntakeChannel = RecruitmentIntakeChannel.Public,
            Notes = $"تقديم عبر البوابة العامة {citizen.ReferenceNumber}",
            EducationLevelCode = citizen.EducationLevelCode,
            Specialization = citizen.Specialization,
            GraduationYear = citizen.GraduationYear,
            YearsOfExperience = citizen.YearsOfExperience,
            BirthDate = citizen.BirthDate
        };
        EvaluateEligibility(app, requisition);
        ApplyDocumentFlag(app, requisition, providedDocumentTypes ?? []);

        db.RecruitmentApplications.Add(app);
        citizen.PromotedRecruitmentApplication = app;
        return app;
    }

    /// <summary>
    /// Stages (no save) the recruitment application of an internal application and links the channel row. A channel
    /// row that is itself new cannot be linked in the same insert (the two rows reference each other), so its link is
    /// set by <see cref="SubmitInternalAsync"/> after the first save.
    /// </summary>
    /// <param name="employeeQualificationCodes">
    /// The employee's <c>Qualification.QualificationCode</c> values; the highest-ranked one becomes the education level.
    /// </param>
    public RecruitmentApplication CreateFromInternal(
        InternalApplication internalApp,
        Employee employee,
        RecruitmentRequisition requisition,
        IEnumerable<string?>? employeeQualificationCodes = null)
    {
        var app = new RecruitmentApplication
        {
            TenantId = internalApp.TenantId,
            ApplicantFullName = employee.FullName,
            // Employee.NationalId is decrypted on materialization; the hash is maintained by the PII interceptor.
            NationalId = employee.NationalId,
            NationalIdHash = employee.NationalIdHash
                ?? (string.IsNullOrWhiteSpace(employee.NationalId) ? null : PiiMask.Sha256Hex(employee.NationalId.Trim())),
            RequisitionId = requisition.Id,
            AppliedPosition = RecruitmentApplicationIntakeRules.DenormalizeJobTitle(requisition),
            AppliedAt = internalApp.AppliedAt,
            Status = RecruitmentApplicationStatus.Submitted,
            IntakeChannel = RecruitmentIntakeChannel.Internal,
            Notes = "ترشّح داخلي",
            SourceInternalApplicationId = internalApp.Id,
            EducationLevelCode = RecruitmentEligibilityRules.HighestRanked(
                employeeQualificationCodes ?? [], options.Value.Eligibility.QualificationRanks),
            BirthDate = employee.BirthDate == default ? null : DateOnly.FromDateTime(employee.BirthDate)
        };
        EvaluateEligibility(app, requisition);

        db.RecruitmentApplications.Add(app);
        if (db.Entry(internalApp).State != EntityState.Added)
            internalApp.PromotedRecruitmentApplication = app;
        return app;
    }

    /// <summary>
    /// HCM-030 (A2): saves a new internal application together with its recruitment application (Submitted) in one
    /// transaction: both rows first, then the channel row's link.
    /// </summary>
    public async Task<RecruitmentApplication> SubmitInternalAsync(
        InternalApplication internalApp,
        Employee employee,
        RecruitmentRequisition requisition,
        CancellationToken ct = default)
    {
        var qualificationCodes = await EmployeeQualificationCodesAsync(employee.Id, ct);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        db.InternalApplications.Add(internalApp);
        var app = CreateFromInternal(internalApp, employee, requisition, qualificationCodes);
        await db.SaveChangesAsync(ct);

        internalApp.PromotedRecruitmentApplicationId = app.Id;
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return app;
    }

    /// <summary>
    /// HCM-030 (A3, C3): links a requisition-linked citizen row created before unification (no link yet) using the
    /// backfill mapping. Returns the linked application (existing or new), or null for a legacy row without requisition.
    /// Like the SQL backfill, it records no application events (historic row — no candidate notification) and leaves
    /// the eligibility flags unevaluated.
    /// </summary>
    public async Task<RecruitmentApplication?> EnsureLinkedAsync(CitizenApplication citizen, CancellationToken ct = default)
    {
        if (citizen.PromotedRecruitmentApplicationId is Guid linkedId)
            return await db.RecruitmentApplications.FirstOrDefaultAsync(a => a.Id == linkedId, ct);
        if (citizen.RequisitionId is not Guid requisitionId)
            return null;

        var requisition = citizen.Requisition
            ?? await db.RecruitmentRequisitions.FirstAsync(r => r.Id == requisitionId, ct);
        var app = CreateFromCitizen(citizen, requisition);
        ApplyLinkState(app, UnifiedIntakeStatusMapping.Map(citizen.Status, citizen.UpdatedAt, requisition));
        app.ConflictOfInterestDeclared = citizen.ConflictOfInterestDeclared;
        app.ConflictOfInterestDeclaredAt = citizen.ConflictOfInterestDeclaredAt;

        using (db.SuppressApplicationEvents())
            await db.SaveChangesAsync(ct);
        return app;
    }

    /// <inheritdoc cref="EnsureLinkedAsync(CitizenApplication, CancellationToken)"/>
    public async Task<RecruitmentApplication?> EnsureLinkedAsync(InternalApplication internalApp, CancellationToken ct = default)
    {
        if (internalApp.PromotedRecruitmentApplicationId is Guid linkedId)
            return await db.RecruitmentApplications.FirstOrDefaultAsync(a => a.Id == linkedId, ct);
        if (internalApp.RequisitionId is not Guid requisitionId)
            return null;

        var requisition = internalApp.Requisition
            ?? await db.RecruitmentRequisitions.FirstAsync(r => r.Id == requisitionId, ct);
        var employee = internalApp.Employee
            ?? await db.Employees.FirstAsync(e => e.Id == internalApp.EmployeeId, ct);
        var app = CreateFromInternal(internalApp, employee, requisition, await EmployeeQualificationCodesAsync(employee.Id, ct));
        ApplyLinkState(app, UnifiedIntakeStatusMapping.Map(internalApp.Status, internalApp.UpdatedAt, requisition));

        using (db.SuppressApplicationEvents())
            await db.SaveChangesAsync(ct);
        return app;
    }

    /// <summary>
    /// HCM-030 (A2): an open (non-terminal) recruitment application of the same person already exists on the
    /// requisition — HR-entered, internal or public.
    /// </summary>
    public Task<bool> HasOpenApplicationAsync(
        Guid requisitionId,
        string? nationalId,
        string? nationalIdHash,
        bool forPublicPortal = false,
        CancellationToken ct = default)
    {
        var normalized = nationalId?.Trim();
        var hash = nationalIdHash ?? (string.IsNullOrEmpty(normalized) ? null : PiiMask.Sha256Hex(normalized));
        if (hash is null && string.IsNullOrEmpty(normalized))
            return Task.FromResult(false);

        var terminal = SelectionWorkflowRules.TerminalStatuses.ToList();
        var applications = forPublicPortal
            ? db.RecruitmentApplications.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            : db.RecruitmentApplications;
        return applications.AnyAsync(
            a => a.RequisitionId == requisitionId
                 && !terminal.Contains(a.Status)
                 && ((hash != null && a.NationalIdHash == hash) || (normalized != null && a.NationalId == normalized)),
            ct);
    }

    /// <summary>
    /// HCM-030 (D2): the requisition's criteria; missing age bounds fall back to
    /// <c>Recruitment:Eligibility:AgeBoundsByRegime</c> for the requisition's service regime (empty by default).
    /// </summary>
    public EligibilityCriteria CriteriaFor(RecruitmentRequisition requisition)
    {
        var regime = LegalSourceToRegimeMapper.MapOfferToRegime(requisition.LegalSourceId);
        var bounds = regime is not null && options.Value.Eligibility.AgeBoundsByRegime.TryGetValue(regime, out var b) ? b : null;
        return new EligibilityCriteria(
            string.IsNullOrWhiteSpace(requisition.MinQualificationCode) ? null : requisition.MinQualificationCode.Trim(),
            requisition.MinYearsOfExperience,
            requisition.MinAge ?? bounds?.MinAge,
            requisition.MaxAge ?? bounds?.MaxAge);
    }

    /// <summary>
    /// HCM-030 (D1): active MDM qualifications of the tenant (or the national platform catalog when the tenant has no
    /// copy), in rank order. Readable from the anonymous portal.
    /// </summary>
    public async Task<IReadOnlyList<QualificationOption>> GetQualificationOptionsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var entries = db.ReferenceCatalogEntries.AsNoTracking()
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.NationalReferenceLookup)
            .Where(e => e.CatalogKind == CatalogKind.Qualification && e.IsActive);
        var rows = await entries.Where(e => e.TenantId == tenantId).ToListAsync(ct);
        if (rows.Count == 0 && tenantId != TenantDefaults.DefaultTenantId)
            rows = await entries.Where(e => e.TenantId == TenantDefaults.DefaultTenantId).ToListAsync(ct);

        var ranks = options.Value.Eligibility.QualificationRanks;
        return rows
            .GroupBy(e => e.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => ranks.TryGetValue(e.Code, out var rank) ? rank : int.MaxValue)
            .ThenBy(e => e.NameAr)
            .Select(e => new QualificationOption(e.Code, e.NameAr))
            .ToList();
    }

    private void EvaluateEligibility(RecruitmentApplication app, RecruitmentRequisition requisition)
    {
        var flags = RecruitmentEligibilityRules.Evaluate(
            new EligibilityProfile(app.EducationLevelCode, app.YearsOfExperience, app.BirthDate),
            CriteriaFor(requisition),
            DateOnly.FromDateTime(app.AppliedAt),
            options.Value.Eligibility.QualificationRanks);
        app.EligibilityFlags = RecruitmentEligibilityRules.Format(flags);
        app.EligibilityEvaluatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// HCM-030 (E4): recomputes only the <c>MissingRequiredDocuments</c> flag from the requisition's required types and
    /// the given document types (no save). Only public applications have an upload path, so the others are not checked
    /// (<see cref="CandidateDocumentTypes.RequiredFor"/>).
    /// </summary>
    public static void ApplyDocumentFlag(
        RecruitmentApplication app,
        RecruitmentRequisition requisition,
        IEnumerable<string> providedDocumentTypes)
    {
        var required = CandidateDocumentTypes.RequiredFor(app.IntakeChannel, requisition.RequiredDocumentTypes);
        var updated = RecruitmentEligibilityRules.WithRequiredDocuments(app.EligibilityFlags, required, providedDocumentTypes);
        if (updated == app.EligibilityFlags)
            return;
        app.EligibilityFlags = updated;
        app.EligibilityEvaluatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// HCM-030 (E4): re-evaluates the document flag after the application's documents changed — saved rows plus the
    /// ones staged in this context (no save). Also called when the candidate uploads missing documents on /careers/my
    /// (<c>CandidatePortalService.UploadDocumentsAsync</c>).
    /// The anonymous portal has no ambient tenant, so the reads bypass the tenant filter and are scoped explicitly to
    /// the application's own tenant.
    /// </summary>
    public async Task ReevaluateDocumentFlagAsync(RecruitmentApplication app, CancellationToken ct = default)
    {
        if (app.RequisitionId is not Guid requisitionId)
            return;
        var tenantId = app.TenantId;
        var requisition = app.Requisition
            ?? await db.RecruitmentRequisitions
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
                .FirstAsync(r => r.Id == requisitionId && r.TenantId == tenantId, ct);
        var saved = await db.CandidateDocuments
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .Where(d => d.TenantId == tenantId && d.RecruitmentApplicationId == app.Id)
            .Select(d => d.DocumentType)
            .ToListAsync(ct);
        var staged = db.CandidateDocuments.Local
            .Where(d => d.RecruitmentApplicationId == app.Id && db.Entry(d).State == EntityState.Added)
            .Select(d => d.DocumentType);
        ApplyDocumentFlag(app, requisition, saved.Concat(staged));
    }

    private async Task<IReadOnlyList<string?>> EmployeeQualificationCodesAsync(Guid employeeId, CancellationToken ct) =>
        await db.Qualifications.AsNoTracking()
            .Where(q => q.EmployeeId == employeeId && q.QualificationCode != null)
            .Select(q => q.QualificationCode)
            .ToListAsync(ct);

    private static void ApplyLinkState(RecruitmentApplication app, UnifiedIntakeLinkState state)
    {
        // Historic row: as the SQL backfill — status from the mapping, eligibility not evaluated.
        app.EligibilityFlags = null;
        app.EligibilityEvaluatedAt = null;
        app.Status = state.Status;
        app.RejectionKind = state.RejectionKind;
        app.RejectedAt = state.RejectedAt;
        app.ScreenedAt = state.ScreenedAt;
        app.ReviewedAt = state.ReviewedAt;
        if (state.Note is not null)
            app.Notes = $"{app.Notes} — {state.Note}";
    }
}
