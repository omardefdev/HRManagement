using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment.Competitions;

/// <summary>HCM-031: one line of a competition as shown on the announcement page.</summary>
public sealed record PublicCompetitionLine(Guid Id, int LineNo, string Title, string GradeCode, int Vacancies, string? OrgUnitName);

/// <summary>HCM-031: the public announcement of a competition (anonymous portal).</summary>
public sealed record PublicCompetitionView(
    RecruitmentCompetition Competition,
    IReadOnlyList<PublicCompetitionLine> Lines,
    bool IntakeOpen,
    IReadOnlyList<string> RequiredDocumentTypes);

/// <summary>HCM-031: result of one item of a bulk screening action.</summary>
public sealed record CompetitionBulkItemResult(Guid ApplicationId, string ApplicantName, bool Success, string? Error);

/// <summary>
/// HCM-031 (PLAN-31, P2): competition intake on top of the PLAN-30 unified intake — one
/// <see cref="RecruitmentApplication"/> per person per competition (<c>CompetitionId</c> set, <c>RequisitionId</c> null)
/// with ordered line preferences; eligibility flags per preferred line (never a rejection); bulk screening through the
/// existing selection transitions (up to <c>Shortlisted</c> = admitted to the exam).
/// </summary>
public sealed class CompetitionIntakeService(
    HRCoreDbContext db,
    UnifiedIntakeService unifiedIntake,
    RecruitmentSelectionService selection,
    IOptions<RecruitmentOptions> options)
{
    public const string DuplicateMessage = "سبق تقديم طلب على هذه المسابقة بهذا الرقم الوطني.";
    public const string IntakeClosedMessage = "التقديم على هذه المسابقة مغلق.";
    public const string PreferencesRequiredMessage = "يرجى اختيار سطر واحد على الأقل بترتيب التفضيل.";
    public const string NotAvailableMessage = "المسابقة غير متاحة للتقديم.";

    /// <summary>The announcement page: visible once announced (and afterwards, read-only) on the public channel.</summary>
    public async Task<PublicCompetitionView?> GetPublicAsync(Guid competitionId, CancellationToken ct = default)
    {
        var competition = await db.RecruitmentCompetitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null
            || competition.AnnouncedAt is null
            || competition.Status == RecruitmentCompetitionStatus.Cancelled
            || !RequisitionWorkflowRules.AllowsPublicChannel(competition.PublishingChannel))
            return null;
        return await BuildViewAsync(competition, ct);
    }

    /// <summary>Finds the competition announced by a posting (the competition posting has no requisition).</summary>
    public Task<Guid?> CompetitionIdForPostingAsync(Guid postingId, CancellationToken ct = default) =>
        db.RecruitmentCompetitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .Where(c => c.PublicJobPostingId == postingId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>Announced competitions open to internal candidates (ESS), in the caller's tenant.</summary>
    public async Task<IReadOnlyList<PublicCompetitionView>> GetInternalOpenAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var competitions = await db.RecruitmentCompetitions.AsNoTracking()
            .Where(c => c.Status == RecruitmentCompetitionStatus.Announced && c.IntakeClosedAt == null && c.ApplicationClosingDate >= today)
            .OrderBy(c => c.ApplicationClosingDate)
            .ToListAsync(ct);
        var views = new List<PublicCompetitionView>();
        foreach (var competition in competitions.Where(c => RequisitionWorkflowRules.AllowsInternalChannel(c.PublishingChannel)))
            views.Add(await BuildViewAsync(competition, ct));
        return views;
    }

    public Task<PublicCompetitionView> GetInternalAsync(RecruitmentCompetition competition, CancellationToken ct = default) =>
        BuildViewAsync(competition, ct);

    public static string? IntakeError(RecruitmentCompetition competition, DateOnly todayUtc) =>
        CompetitionWorkflowRules.IsIntakeOpen(competition.Status, competition.ApplicationClosingDate, competition.IntakeClosedAt, todayUtc)
            ? null
            : IntakeClosedMessage;

    /// <summary>
    /// Ordered preferences: 1..<c>MaxLinesPerCandidate</c> distinct lines of the competition, the first one required
    /// (empty slots are ignored, gaps are closed).
    /// </summary>
    public static string? ValidatePreferences(
        RecruitmentCompetition competition,
        IReadOnlyCollection<Guid> lineIds,
        IReadOnlyList<Guid?> preferences,
        out IReadOnlyList<Guid> ordered)
    {
        var chosen = preferences.Where(p => p is not null).Select(p => p!.Value).ToList();
        ordered = chosen;
        if (chosen.Count == 0)
            return PreferencesRequiredMessage;
        if (chosen.Count > competition.MaxLinesPerCandidate)
            return $"يمكن اختيار {competition.MaxLinesPerCandidate} أسطر على الأكثر.";
        if (chosen.Distinct().Count() != chosen.Count)
            return "لا يجوز تكرار السطر نفسه في التفضيلات.";
        if (chosen.Any(id => !lineIds.Contains(id)))
            return "أحد الأسطر المختارة لا يتبع هذه المسابقة.";
        return null;
    }

    public Task<bool> HasApplicationAsync(Guid tenantId, Guid competitionId, string nationalIdHash, bool forPublicPortal, CancellationToken ct = default)
    {
        var applications = forPublicPortal
            ? db.RecruitmentApplications.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            : db.RecruitmentApplications;
        return applications.AnyAsync(a => a.TenantId == tenantId && a.CompetitionId == competitionId && a.NationalIdHash == nationalIdHash, ct);
    }

    /// <summary>Loads the competition with its lines and their requisitions (for eligibility), bypassing the tenant filter for the portal.</summary>
    public Task<RecruitmentCompetition?> LoadForIntakeAsync(Guid competitionId, bool forPublicPortal, CancellationToken ct = default)
    {
        var competitions = forPublicPortal
            ? db.RecruitmentCompetitions.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            : db.RecruitmentCompetitions;
        return competitions
            .Include(c => c.Lines).ThenInclude(l => l.Requisitions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);
    }

    /// <summary>Stages (no save) the competition application of a new public application and links the citizen row.</summary>
    public RecruitmentApplication CreateFromCitizen(
        CitizenApplication citizen,
        RecruitmentCompetition competition,
        IReadOnlyList<Guid> orderedLineIds,
        IEnumerable<string>? providedDocumentTypes = null)
    {
        var app = new RecruitmentApplication
        {
            TenantId = citizen.TenantId,
            ApplicantFullName = citizen.FullName,
            NationalId = citizen.NationalId,
            NationalIdHash = citizen.NationalIdHash ?? PiiMask.Sha256Hex(citizen.NationalId.Trim()),
            CompetitionId = competition.Id,
            RequisitionId = null,
            AppliedPosition = Truncate(competition.Title, 200),
            AppliedAt = citizen.SubmittedAt,
            Status = RecruitmentApplicationStatus.Submitted,
            IntakeChannel = RecruitmentIntakeChannel.Public,
            Notes = $"تقديم على المسابقة {competition.Number} عبر البوابة العامة {citizen.ReferenceNumber}",
            EducationLevelCode = citizen.EducationLevelCode,
            Specialization = citizen.Specialization,
            GraduationYear = citizen.GraduationYear,
            YearsOfExperience = citizen.YearsOfExperience,
            BirthDate = citizen.BirthDate
        };
        StagePreferences(app, competition, orderedLineIds);
        var required = CandidateDocumentTypes.RequiredFor(app.IntakeChannel, RequiredDocumentTypesOf(competition));
        app.EligibilityFlags = RecruitmentEligibilityRules.WithRequiredDocuments(app.EligibilityFlags, required, providedDocumentTypes ?? []);

        db.RecruitmentApplications.Add(app);
        citizen.PromotedRecruitmentApplication = app;
        return app;
    }

    /// <summary>Saves a new internal competition application with its channel row in one transaction (as the PLAN-30 internal path).</summary>
    public async Task<RecruitmentApplication> SubmitInternalAsync(
        InternalApplication internalApp,
        Employee employee,
        RecruitmentCompetition competition,
        IReadOnlyList<Guid> orderedLineIds,
        CancellationToken ct = default)
    {
        var qualificationCodes = await db.Qualifications.AsNoTracking()
            .Where(q => q.EmployeeId == employee.Id && q.QualificationCode != null)
            .Select(q => q.QualificationCode)
            .ToListAsync(ct);
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        db.InternalApplications.Add(internalApp);
        var app = new RecruitmentApplication
        {
            TenantId = internalApp.TenantId,
            ApplicantFullName = employee.FullName,
            NationalId = employee.NationalId,
            NationalIdHash = employee.NationalIdHash
                ?? (string.IsNullOrWhiteSpace(employee.NationalId) ? null : PiiMask.Sha256Hex(employee.NationalId.Trim())),
            CompetitionId = competition.Id,
            RequisitionId = null,
            AppliedPosition = Truncate(competition.Title, 200),
            AppliedAt = internalApp.AppliedAt,
            Status = RecruitmentApplicationStatus.Submitted,
            IntakeChannel = RecruitmentIntakeChannel.Internal,
            Notes = $"ترشّح داخلي على المسابقة {competition.Number}",
            SourceInternalApplicationId = internalApp.Id,
            EducationLevelCode = RecruitmentEligibilityRules.HighestRanked(qualificationCodes, options.Value.Eligibility.QualificationRanks),
            BirthDate = employee.BirthDate == default ? null : DateOnly.FromDateTime(employee.BirthDate)
        };
        StagePreferences(app, competition, orderedLineIds);
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync(ct);

        internalApp.PromotedRecruitmentApplicationId = app.Id;
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return app;
    }

    /// <summary>
    /// Union of the attached requisitions' required document types (comma-separated, as stored on a requisition), so a
    /// competition asks for what any of its vacancies asks for.
    /// </summary>
    public static string? RequiredDocumentTypesOf(RecruitmentCompetition competition) =>
        CandidateDocumentTypes.Format(competition.Lines
            .SelectMany(l => l.Requisitions)
            .SelectMany(r => CandidateDocumentTypes.Parse(r.RequiredDocumentTypes)));

    /// <summary>The same union, read from the database (for the document-flag re-evaluation; anonymous portal safe).</summary>
    public static async Task<string?> RequiredDocumentTypesAsync(HRCoreDbContext db, Guid tenantId, Guid competitionId, CancellationToken ct)
    {
        var stored = await db.RecruitmentRequisitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .Where(r => r.TenantId == tenantId && r.CompetitionLine != null && r.CompetitionLine.CompetitionId == competitionId)
            .Select(r => r.RequiredDocumentTypes)
            .ToListAsync(ct);
        return CandidateDocumentTypes.Format(stored.SelectMany(CandidateDocumentTypes.Parse));
    }

    /// <summary>
    /// HR bulk screening on the «المتقدمون» tab. Targets: UnderReview, Shortlisted (admitted to the exam; the actor's
    /// conflict-of-interest declaration is recorded first, and a flagged application needs <paramref name="eligibilityOverrideReason"/>)
    /// or Rejected. Every item goes through <see cref="RecruitmentSelectionService.TransitionStatusAsync"/>; failures are listed.
    /// </summary>
    public async Task<IReadOnlyList<CompetitionBulkItemResult>> BulkTransitionAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> applicationIds,
        RecruitmentApplicationStatus target,
        bool conflictOfInterestDeclared,
        string? eligibilityOverrideReason,
        string? rejectionNote,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        if (target is not (RecruitmentApplicationStatus.UnderReview or RecruitmentApplicationStatus.Shortlisted or RecruitmentApplicationStatus.Rejected))
            return [new CompetitionBulkItemResult(Guid.Empty, "—", false, "الإجراء الجماعي غير مسموح لهذه الحالة.")];

        var competitionStatus = await db.RecruitmentCompetitions.Where(c => c.Id == competitionId).Select(c => (RecruitmentCompetitionStatus?)c.Status).FirstOrDefaultAsync(ct);
        if (competitionStatus is not (RecruitmentCompetitionStatus.Announced or RecruitmentCompetitionStatus.IntakeClosed))
            return [new CompetitionBulkItemResult(Guid.Empty, "—", false, "الفرز متاح بعد الإعلان وحتى بدء الامتحانات.")];

        var applications = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.CompetitionId == competitionId && applicationIds.Contains(a.Id))
            .Select(a => new { a.Id, a.ApplicantFullName, a.Status, a.ConflictOfInterestDeclared })
            .ToListAsync(ct);

        var results = new List<CompetitionBulkItemResult>();
        foreach (var application in applications)
        {
            if (target == RecruitmentApplicationStatus.Shortlisted && !application.ConflictOfInterestDeclared)
            {
                if (!conflictOfInterestDeclared)
                {
                    results.Add(new(application.Id, application.ApplicantFullName, false, "يجب الإقرار بعدم تعارض المصالح قبل القبول للاختبار."));
                    continue;
                }
                var tracked = await db.RecruitmentApplications.FirstAsync(a => a.Id == application.Id, ct);
                var (declared, declareError) = await selection.DeclareCoiAsync(tracked, actorUserId, ct);
                if (!declared)
                {
                    results.Add(new(application.Id, application.ApplicantFullName, false, declareError));
                    continue;
                }
            }

            // Shortlisting is reached through UnderReview (the selection transition map).
            if (target == RecruitmentApplicationStatus.Shortlisted && application.Status == RecruitmentApplicationStatus.Submitted)
            {
                var (reviewed, reviewError) = await selection.TransitionStatusAsync(
                    application.Id, RecruitmentApplicationStatus.UnderReview, legacyHireWithoutOffer: false, actorUserId: actorUserId, ct: ct);
                if (!reviewed)
                {
                    results.Add(new(application.Id, application.ApplicantFullName, false, reviewError));
                    continue;
                }
            }

            var (ok, error) = await selection.TransitionStatusAsync(
                application.Id, target, legacyHireWithoutOffer: false, rejectionNote, eligibilityOverrideReason, actorUserId, ct);
            results.Add(new(application.Id, application.ApplicantFullName, ok, ok ? null : error));
        }

        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = CompetitionLifecycleService.EntityType,
            EntityId = competitionId,
            Action = "BulkScreenApplications",
            UserId = actorUserId,
            ChangedFields = JsonSerializer.Serialize(new { target = target.ToString(), succeeded = results.Count(r => r.Success), failed = results.Count(r => !r.Success) })
        });
        await db.SaveChangesAsync(ct);
        return results;
    }

    /// <summary>
    /// Stages the preference rows and evaluates the PLAN-30 eligibility flags against every requisition of each preferred
    /// line (union per line on the preference; union over the lines on the application).
    /// </summary>
    private void StagePreferences(RecruitmentApplication app, RecruitmentCompetition competition, IReadOnlyList<Guid> orderedLineIds)
    {
        var profile = new EligibilityProfile(app.EducationLevelCode, app.YearsOfExperience, app.BirthDate);
        var applied = DateOnly.FromDateTime(app.AppliedAt);
        var ranks = options.Value.Eligibility.QualificationRanks;
        var all = new SortedSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < orderedLineIds.Count; i++)
        {
            var line = competition.Lines.First(l => l.Id == orderedLineIds[i]);
            var lineFlags = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var requisition in line.Requisitions)
                foreach (var flag in RecruitmentEligibilityRules.Evaluate(profile, unifiedIntake.CriteriaFor(requisition), applied, ranks))
                    lineFlags.Add(flag);
            all.UnionWith(lineFlags);
            db.CompetitionPreferences.Add(new CompetitionPreference
            {
                TenantId = app.TenantId,
                RecruitmentApplicationId = app.Id,
                CompetitionLineId = line.Id,
                Order = i + 1,
                EligibilityFlags = RecruitmentEligibilityRules.Format(lineFlags)
            });
        }
        app.EligibilityFlags = RecruitmentEligibilityRules.Format(all);
        app.EligibilityEvaluatedAt = DateTime.UtcNow;
    }

    private async Task<PublicCompetitionView> BuildViewAsync(RecruitmentCompetition competition, CancellationToken ct)
    {
        var lines = await db.CompetitionLines
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PublicCareersPortal)
            .AsNoTracking()
            .Where(l => l.TenantId == competition.TenantId && l.CompetitionId == competition.Id)
            .OrderBy(l => l.LineNo)
            .Select(l => new PublicCompetitionLine(
                l.Id, l.LineNo, l.Title, l.GradeCode, l.Requisitions.Count, l.OrgUnit != null ? l.OrgUnit.NameAr : null))
            .ToListAsync(ct);
        var required = await RequiredDocumentTypesAsync(db, competition.TenantId, competition.Id, ct);
        return new PublicCompetitionView(
            competition,
            lines,
            IntakeError(competition, DateOnly.FromDateTime(DateTime.UtcNow)) is null,
            CandidateDocumentTypes.Parse(required));
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
