using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-030 (A1, G-16): one intake path. Every public or internal application on a requisition is a
/// <see cref="RecruitmentApplication"/> from the moment it is submitted; the channel row
/// (<see cref="CitizenApplication"/> / <see cref="InternalApplication"/>) keeps the submitted data and links to it
/// through its existing <c>PromotedRecruitmentApplicationId</c>.
/// </summary>
public sealed class UnifiedIntakeService(HRCoreDbContext db)
{
    public const string ManagedByUnifiedPathMessage = "يُدار هذا الطلب من مسار التوظيف الموحد.";

    /// <summary>Stages (no save) the recruitment application of a new public application and links the citizen row.</summary>
    public RecruitmentApplication CreateFromCitizen(CitizenApplication citizen, RecruitmentRequisition requisition)
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
            Notes = $"تقديم عبر البوابة العامة {citizen.ReferenceNumber}"
        };

        db.RecruitmentApplications.Add(app);
        citizen.PromotedRecruitmentApplication = app;
        return app;
    }

    /// <summary>
    /// Stages (no save) the recruitment application of an internal application and links the channel row. A channel
    /// row that is itself new cannot be linked in the same insert (the two rows reference each other), so its link is
    /// set by <see cref="SubmitInternalAsync"/> after the first save.
    /// </summary>
    public RecruitmentApplication CreateFromInternal(
        InternalApplication internalApp,
        Employee employee,
        RecruitmentRequisition requisition)
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
            SourceInternalApplicationId = internalApp.Id
        };

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
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        db.InternalApplications.Add(internalApp);
        var app = CreateFromInternal(internalApp, employee, requisition);
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
    /// Like the SQL backfill, it records no application events (historic row — no candidate notification).
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
        var app = CreateFromInternal(internalApp, employee, requisition);
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

    private static void ApplyLinkState(RecruitmentApplication app, UnifiedIntakeLinkState state)
    {
        app.Status = state.Status;
        app.RejectionKind = state.RejectionKind;
        app.RejectedAt = state.RejectedAt;
        app.ScreenedAt = state.ScreenedAt;
        app.ReviewedAt = state.ReviewedAt;
        if (state.Note is not null)
            app.Notes = $"{app.Notes} — {state.Note}";
    }
}
