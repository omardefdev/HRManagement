using System.Diagnostics;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>What started a retention run and whether it may anonymize.</summary>
/// <param name="UtcNow">"Now" for the retention rule (injected so tests do not depend on the clock).</param>
/// <param name="Apply">Anonymize due pairs; honoured only when <c>Recruitment:Retention:Apply</c> is true too.</param>
/// <param name="TriggeredBy"><see cref="CandidateRetentionRun.JobTrigger"/> or the admin's user id.</param>
/// <param name="TenantId">One tenant (admin screen), or null for every tenant (the job).</param>
/// <param name="SkipTenantsCompletedWithin">
/// The job's interval: a tenant is skipped when it already has a <em>finished</em> job run started within it, so a cycle cut
/// short (restart, cancellation, a failure) resumes with the tenants it did not finish instead of waiting a whole interval.
/// Checked inside the advisory lock. Null for manual runs.
/// </param>
public sealed record CandidateRetentionRunRequest(
    DateTime UtcNow, bool Apply, string TriggeredBy, Guid? TenantId = null, TimeSpan? SkipTenantsCompletedWithin = null);

/// <summary>How the trigger date of a due row was chosen.</summary>
public enum CandidateRetentionTriggerKind
{
    RequisitionClosed,
    RequisitionCancelled,
    Applied
}

/// <summary>One row of the admin due list: no applicant data beyond the reference.</summary>
public sealed record CandidateRetentionDueItem(
    Guid Id,
    bool IsLegacyCitizen,
    string Reference,
    string? RequisitionNumber,
    DateTime TriggerDate,
    CandidateRetentionTriggerKind TriggerKind,
    bool PartiallyAnonymized);

/// <summary>Counts of the due list of one tenant.</summary>
public sealed record CandidateRetentionCounts(int Due, int SkippedLegalHold, int SkippedAppeal);

/// <summary>
/// HCM-030 (G1/G2): pair-aware candidate retention. A pair is a <see cref="RecruitmentApplication"/> and its linked citizen
/// or internal channel row; it is due when the application is not hired, neither row is on legal hold (internal rows carry
/// none), no appeal is pending, the pair still holds data (never anonymized, half-anonymized — e.g. a DSAR erasure of the
/// citizen row only — or anonymized before HCM-030 with documents or messages left), and its trigger date (requisition
/// <c>ClosedDate</c> ?? <c>CancelledAt</c> ?? <c>AppliedAt</c>; open requisitions never trigger) is at least
/// <c>Recruitment:Retention:Months</c> old. Unlinked (legacy) citizen rows keep the HCM-006 rule
/// (<see cref="CandidateApplicationLegalLifecycleService.CanPurge(CitizenApplication, RecruitmentRequisition?, DateTime, int)"/>).
/// <para>
/// Anonymization runs in one transaction per pair: both rows through <c>TryAnonymize</c>, the internal row's free text,
/// every <see cref="CandidateDocument"/> file released through the File.Storage reference outbox (explicit tenant) and its
/// row deleted, every <see cref="CandidateNotification"/> neutralized (pending ones suppressed), the /careers/my code
/// challenges deleted, the on-behalf offer evidence released and the candidate free text of offers and appeals neutralized
/// (PLAN-30 K1), and one PII-free audit row.
/// A run writes one <see cref="CandidateRetentionRun"/> per tenant (counts only).
/// </para>
/// <para>
/// The job runs across tenants (<see cref="SystemTenantBypassPurpose.BackgroundQueueProcessing"/>) with an explicit tenant
/// predicate per tenant; a run from the admin screen stays under the tenant filter of the admin's tenant.
/// </para>
/// </summary>
public sealed class CandidateRetentionService(
    HRCoreDbContext db,
    IFileReferenceOutbox fileReferenceOutbox,
    IOptions<RecruitmentOptions> options,
    ILogger<CandidateRetentionService> logger)
{
    /// <summary>What a neutralized notification keeps as body and recipient (both columns are required).</summary>
    public const string NeutralizedText = "—";

    /// <summary><c>LastError</c> of a pending notification suppressed by anonymization.</summary>
    public const string SuppressedReason = "Anonymized (retention)";

    public const string AnonymizedAuditAction = "CandidateRetentionAnonymized";

    private RecruitmentRetentionOptions Settings => options.Value.Retention;

    private int Months => Math.Max(1, Settings.Months);

    private int BatchSize => Math.Clamp(Settings.BatchSize, 1, 1000);

    /// <summary>
    /// Counts (and, when <see cref="CandidateRetentionRunRequest.Apply"/> and <c>Recruitment:Retention:Apply</c> are both
    /// true, anonymizes) the due pairs of one tenant or of every tenant, and logs one run row per tenant.
    /// </summary>
    public async Task<IReadOnlyList<CandidateRetentionRun>> RunAsync(CandidateRetentionRunRequest request, CancellationToken ct = default)
    {
        var apply = request.Apply && Settings.Apply;
        var system = request.TenantId is null;
        var tenants = request.TenantId is Guid one ? [one] : await TenantsWithCandidatesAsync(ct);
        if (system && request.SkipTenantsCompletedWithin is TimeSpan within)
        {
            var since = request.UtcNow - within;
            var completed = await db.CandidateRetentionRuns
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
                .Where(r => r.TriggeredBy == CandidateRetentionRun.JobTrigger && r.FinishedAt != null && r.StartedAt > since)
                .Select(r => r.TenantId)
                .Distinct()
                .ToListAsync(ct);
            tenants = tenants.Except(completed).ToList();
        }

        var runs = new List<CandidateRetentionRun>();
        foreach (var tenantId in tenants)
        {
            ct.ThrowIfCancellationRequested();
            runs.Add(await RunTenantAsync(tenantId, system, apply, request, ct));
        }
        return runs;
    }

    /// <summary>Counts of the due list of one tenant (the screen and the dry run).</summary>
    public async Task<CandidateRetentionCounts> CountAsync(Guid tenantId, DateTime utcNow, CancellationToken ct = default) =>
        await CountAsync(new Scope(tenantId, System: false), utcNow, ct);

    /// <summary>
    /// One page of the due list of one tenant: due pairs first (oldest application first), then due legacy citizen rows.
    /// Pairs on legal hold or with a pending appeal are not listed (they are counted).
    /// </summary>
    public async Task<(IReadOnlyList<CandidateRetentionDueItem> Items, int TotalCount)> GetDuePageAsync(
        Guid tenantId, DateTime utcNow, int page, int pageSize, CancellationToken ct = default)
    {
        var scope = new Scope(tenantId, System: false);
        var cutoff = CandidateApplicationLegalLifecycleService.RetentionCutoff(utcNow, Months);
        var pairs = DuePairs(scope, cutoff);
        var legacy = DueLegacy(scope, cutoff);
        var pairCount = await pairs.CountAsync(ct);
        var legacyCount = await legacy.CountAsync(ct);

        var skip = (Math.Max(1, page) - 1) * pageSize;
        var items = new List<CandidateRetentionDueItem>();
        if (skip < pairCount)
            items.AddRange(await LoadPairItemsAsync(scope, pairs.OrderBy(a => a.AppliedAt).ThenBy(a => a.Id).Skip(skip).Take(pageSize), ct));
        var legacySkip = Math.Max(0, skip - pairCount);
        var legacyTake = pageSize - items.Count;
        if (legacyTake > 0 && legacySkip < legacyCount)
            items.AddRange(await LoadLegacyItemsAsync(legacy.OrderBy(c => c.SubmittedAt).ThenBy(c => c.Id).Skip(legacySkip).Take(legacyTake), ct));

        return (items, pairCount + legacyCount);
    }

    /// <summary>Latest runs of one tenant (newest first).</summary>
    public Task<List<CandidateRetentionRun>> GetRunsAsync(Guid tenantId, int take, CancellationToken ct = default) =>
        db.CandidateRetentionRuns.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.StartedAt)
            .Take(take)
            .ToListAsync(ct);

    // ------------------------------------------------------------------ run

    private async Task<CandidateRetentionRun> RunTenantAsync(
        Guid tenantId, bool system, bool apply, CandidateRetentionRunRequest request, CancellationToken ct)
    {
        var scope = new Scope(tenantId, system);
        var clock = Stopwatch.StartNew();
        var run = new CandidateRetentionRun
        {
            TenantId = tenantId,
            StartedAt = request.UtcNow,
            DryRun = !apply,
            TriggeredBy = Truncate(request.TriggeredBy, 64) ?? CandidateRetentionRun.JobTrigger
        };

        // A run that did not get through all of its rows (a run-level failure or a cancellation) keeps FinishedAt null:
        // the job then treats the tenant as not done and picks it up again on its next hourly check.
        var finished = true;
        var failures = new List<string>();
        try
        {
            var counts = await CountAsync(scope, request.UtcNow, ct);
            run.DueCount = counts.Due;
            run.SkippedLegalHold = counts.SkippedLegalHold;
            run.SkippedAppeal = counts.SkippedAppeal;

            if (apply && counts.Due > 0)
                await ApplyAsync(scope, request, run, failures, ct);
        }
        catch (OperationCanceledException)
        {
            // Pairs committed before the cancellation stay anonymized: log the run with its partial count, then stop.
            failures.Add("cancelled");
            try
            {
                await SaveRunAsync(run, failures, finishedAt: null);
            }
            catch (Exception saveError)
            {
                logger.LogError(saveError, "Candidate retention could not log the cancelled run for tenant {TenantId}.", tenantId);
            }
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Candidate retention run failed for tenant {TenantId}.", tenantId);
            failures.Add("run: " + ex.GetType().Name);
            finished = false;
        }

        await SaveRunAsync(run, failures, finished ? request.UtcNow + clock.Elapsed : null);
        return run;
    }

    /// <summary>Saves the run row, never with the caller's token: an interrupted apply must still be logged.</summary>
    private async Task SaveRunAsync(CandidateRetentionRun run, List<string> failures, DateTime? finishedAt)
    {
        db.ChangeTracker.Clear();
        if (failures.Count > 0)
            run.Error = Truncate($"{failures.Count} failure(s): {string.Join(", ", failures.Distinct().Take(10))}", 1000);
        run.FinishedAt = finishedAt;
        db.CandidateRetentionRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Anonymizes the due rows; <see cref="CandidateRetentionRun.AnonymizedCount"/> grows with each commit.</summary>
    private async Task ApplyAsync(
        Scope scope, CandidateRetentionRunRequest request, CandidateRetentionRun run, List<string> failures, CancellationToken ct)
    {
        var cutoff = CandidateApplicationLegalLifecycleService.RetentionCutoff(request.UtcNow, Months);
        var actor = Guid.TryParse(request.TriggeredBy, out var userId) ? userId : (Guid?)null;

        // Ids first (a few bytes each), then the pairs in batches: anonymized pairs leave the due query, so paging the
        // live query would skip rows; a failed pair stays due and is retried by the next run.
        var pairIds = await DuePairs(scope, cutoff).OrderBy(a => a.AppliedAt).ThenBy(a => a.Id).Select(a => a.Id).ToListAsync(ct);
        foreach (var batch in pairIds.Chunk(BatchSize))
        {
            foreach (var id in batch)
            {
                if (await TryAnonymizeAsync(() => AnonymizePairAsync(scope, id, request, actor, ct), failures, ct))
                    run.AnonymizedCount++;
            }
            db.ChangeTracker.Clear();
        }

        var legacyIds = await DueLegacy(scope, cutoff).OrderBy(c => c.SubmittedAt).ThenBy(c => c.Id).Select(c => c.Id).ToListAsync(ct);
        foreach (var batch in legacyIds.Chunk(BatchSize))
        {
            foreach (var id in batch)
            {
                if (await TryAnonymizeAsync(() => AnonymizeLegacyCitizenAsync(scope, id, request, actor, ct), failures, ct))
                    run.AnonymizedCount++;
            }
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>One transaction; on failure it is rolled back, the tracker cleared and the failure recorded.</summary>
    private async Task<bool> TryAnonymizeAsync(Func<Task<bool>> anonymize, List<string> failures, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            if (!await anonymize())
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return false;
            }

            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Candidate retention could not anonymize one pair.");
            failures.Add(ex.GetType().Name);
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>Stages the anonymization of one pair (no save); false when it is no longer due.</summary>
    private async Task<bool> AnonymizePairAsync(
        Scope scope, Guid applicationId, CandidateRetentionRunRequest request, Guid? actor, CancellationToken ct)
    {
        await LockPairRowsAsync(scope, applicationId, ct);
        var application = await Query<RecruitmentApplication>(scope).Include(a => a.Requisition).Include(a => a.Competition)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (application is null)
            return false;

        var citizens = await Query<CitizenApplication>(scope)
            .Where(c => c.PromotedRecruitmentApplicationId == applicationId)
            .ToListAsync(ct);
        var internals = await Query<InternalApplication>(scope)
            .Where(i => i.PromotedRecruitmentApplicationId == applicationId
                || (application.SourceInternalApplicationId != null && i.Id == application.SourceInternalApplicationId))
            .ToListAsync(ct);
        var pendingAppeal = await Query<RecruitmentAppeal>(scope)
            .AnyAsync(x => x.RecruitmentApplicationId == applicationId && x.Status == RecruitmentAppealStatus.Pending, ct);

        // Re-checked inside the transaction, after the row locks: a hold, an appeal or a hire committed since the ids were
        // read is seen here, and one committed later waits for this transaction (it updates a locked row).
        var verdict = CandidateApplicationLegalLifecycleService.EvaluatePair(
            application, application.Requisition, citizens.FirstOrDefault(c => c.LegalHold) ?? citizens.FirstOrDefault(),
            pendingAppeal, request.UtcNow, Months, residualData: true);
        if (verdict != CandidateRetentionVerdict.Due)
            return false;

        if (!CandidateApplicationLegalLifecycleService.TryAnonymize(application, request.UtcNow).Success)
            return false;
        foreach (var citizen in citizens)
        {
            if (!CandidateApplicationLegalLifecycleService.TryAnonymize(citizen, request.UtcNow).Success)
                return false;
        }
        foreach (var internalRow in internals)
            CandidateApplicationLegalLifecycleService.ClearFreeText(internalRow);

        var citizenIds = citizens.Select(c => (Guid?)c.Id).ToList();
        var documents = await Query<CandidateDocument>(scope)
            .Where(d => d.RecruitmentApplicationId == applicationId || citizenIds.Contains(d.CitizenApplicationId))
            .ToListAsync(ct);
        ReleaseAndDelete(scope.TenantId, documents);

        var notifications = await NeutralizeNotificationsAsync(scope, applicationId, ct);

        // PLAN-30 K1: the pair's /careers/my code challenges are deleted (TryAnonymize cleared the lock and the last
        // request time) and the on-behalf offer evidence files are released.
        var otpChallenges = await DeleteOtpChallengesAsync(scope, citizens.Select(c => c.Id).ToList(), ct);
        var offerEvidence = await ReleaseOfferEvidenceAsync(scope, applicationId, ct);
        // PLAN-30 K1: free text typed by or about the candidate on offers and appeals (the /careers/my decline, withdraw
        // and appeal forms, the on-behalf acceptance note). Statuses, dates and the channel (AcceptedBy/DeclinedBy/FiledBy)
        // are kept; HR's own decision text is not candidate data and stays.
        var freeTextRows = await NeutralizeOfferAndAppealTextAsync(scope, applicationId, ct);
        freeTextRows += await ClearCompetitionDataAsync(scope, applicationId, ct);

        AddAudit(scope.TenantId, nameof(RecruitmentApplication), applicationId, actor, request.TriggeredBy,
            citizens.Count, internals.Count, documents.Count, notifications, otpChallenges, offerEvidence, freeTextRows);
        return true;
    }

    /// <summary>
    /// HCM-031 (PLAN-31): competition data of the pair (no save). The exam number, marks and results are kept (the
    /// results are an immutable published record keyed by exam number; the application row itself is anonymized), while
    /// what identified the candidate is cleared: the admission card hash (it covers the name), the per-line eligibility
    /// notes and the markers' notes. Objection text is neutralized with the other appeal text.
    /// </summary>
    private async Task<int> ClearCompetitionDataAsync(Scope scope, Guid applicationId, CancellationToken ct)
    {
        var rows = 0;
        foreach (var paper in await Query<ExamCandidate>(scope)
                     .Where(c => c.RecruitmentApplicationId == applicationId && (c.AdmissionCardSha256 != null || c.AdmissionCardIssuedAt != null))
                     .ToListAsync(ct))
        {
            paper.AdmissionCardSha256 = null;
            paper.AdmissionCardIssuedAt = null;
            rows++;
        }
        foreach (var preference in await Query<CompetitionPreference>(scope)
                     .Where(p => p.RecruitmentApplicationId == applicationId && p.EligibilityFlags != null)
                     .ToListAsync(ct))
        {
            preference.EligibilityFlags = null;
            rows++;
        }
        var paperIds = await Query<ExamCandidate>(scope).Where(c => c.RecruitmentApplicationId == applicationId).Select(c => c.Id).ToListAsync(ct);
        foreach (var score in await Query<ExamScore>(scope).Where(s => paperIds.Contains(s.ExamCandidateId) && s.Note != null).ToListAsync(ct))
        {
            score.Note = null;
            rows++;
        }
        return rows;
    }

    /// <summary>
    /// PostgreSQL: locks the pair's application row and its citizen rows (<c>FOR UPDATE</c>) until the pair's transaction
    /// ends, so the verdict cannot be overtaken by a concurrent legal hold (<c>citizen/legal-hold</c> updates the citizen
    /// row), appeal (<c>RecruitmentAppealService</c> sets the application's <c>AppealStatus</c>) or hire
    /// (<c>HiredEmployeeId</c>); the reads that follow see whatever was committed before the locks were granted.
    /// Other providers (SQLite unit tests) have no row locks.
    /// </summary>
    private async Task LockPairRowsAsync(Scope scope, Guid applicationId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "RecruitmentApplications" WHERE "Id" = {applicationId} AND "TenantId" = {scope.TenantId} FOR UPDATE""",
            ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "CitizenApplications" WHERE "PromotedRecruitmentApplicationId" = {applicationId} AND "TenantId" = {scope.TenantId} ORDER BY "Id" FOR UPDATE""",
            ct);
    }

    /// <summary>Stages the anonymization of one legacy unlinked citizen row (no save); false when it is no longer due.</summary>
    private async Task<bool> AnonymizeLegacyCitizenAsync(
        Scope scope, Guid citizenId, CandidateRetentionRunRequest request, Guid? actor, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            // Same reason as LockPairRowsAsync: a concurrent legal hold waits for this transaction or is seen below.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "CitizenApplications" WHERE "Id" = {citizenId} AND "TenantId" = {scope.TenantId} FOR UPDATE""",
                ct);
        }
        var citizen = await Query<CitizenApplication>(scope).Include(c => c.Requisition)
            .FirstOrDefaultAsync(c => c.Id == citizenId, ct);
        if (citizen is null
            || !CandidateApplicationLegalLifecycleService.CanPurge(citizen, citizen.Requisition, request.UtcNow, Months)
            || !CandidateApplicationLegalLifecycleService.TryAnonymize(citizen, request.UtcNow).Success)
            return false;

        var documents = await Query<CandidateDocument>(scope)
            .Where(d => d.CitizenApplicationId == citizenId)
            .ToListAsync(ct);
        ReleaseAndDelete(scope.TenantId, documents);

        // PLAN-30 K1: the row's /careers/my code challenges (an unlinked row has no offer).
        var otpChallenges = await DeleteOtpChallengesAsync(scope, [citizenId], ct);

        AddAudit(scope.TenantId, nameof(CitizenApplication), citizenId, actor, request.TriggeredBy, 1, 0, documents.Count, 0,
            otpChallenges, 0, 0);
        return true;
    }

    /// <summary>
    /// Releases each file's <c>hr-core:CandidateDocument:{Id:N}:FileId</c> reference through the outbox and deletes the
    /// rows. The outbox stamps the ambient tenant (none in the job), so the release rows get the pair's tenant explicitly.
    /// </summary>
    private void ReleaseAndDelete(Guid tenantId, IReadOnlyList<CandidateDocument> documents)
    {
        if (documents.Count == 0)
            return;

        foreach (var document in documents)
            fileReferenceOutbox.EnqueueRelease(document.FileId, HolderKey(document));
        StampReleaseTenant(tenantId, documents.Select(d => d.FileId).ToHashSet());

        db.CandidateDocuments.RemoveRange(documents);
    }

    private void StampReleaseTenant(Guid tenantId, IReadOnlySet<Guid> fileIds)
    {
        foreach (var entry in db.ChangeTracker.Entries<FileReferenceOutboxMessage>())
        {
            if (entry.State == EntityState.Added
                && entry.Entity.Operation == FileReferenceOutboxOps.Release
                && fileIds.Contains(entry.Entity.FileId))
                entry.Entity.TenantId = tenantId;
        }
    }

    /// <summary>PLAN-30 K1: deletes the /careers/my code challenges of the given citizen rows; returns how many.</summary>
    private async Task<int> DeleteOtpChallengesAsync(Scope scope, List<Guid> citizenIds, CancellationToken ct)
    {
        if (citizenIds.Count == 0)
            return 0;
        var challenges = await Query<CandidateOtpChallenge>(scope)
            .Where(o => citizenIds.Contains(o.CitizenApplicationId))
            .ToListAsync(ct);
        db.CandidateOtpChallenges.RemoveRange(challenges);
        return challenges.Count;
    }

    /// <summary>
    /// PLAN-30 K1: releases the on-behalf acceptance/decline evidence of the pair's offers
    /// (<c>hr-core:OfferLetter:{Id:N}:AcceptanceEvidence</c> / <c>…:DeclineEvidence</c>, the keys
    /// <c>OfferOnBehalfService</c> registered) with the pair's tenant, and clears the ids; returns how many files.
    /// </summary>
    private async Task<int> ReleaseOfferEvidenceAsync(Scope scope, Guid applicationId, CancellationToken ct)
    {
        var offers = await Query<OfferLetter>(scope)
            .Where(o => o.RecruitmentApplicationId == applicationId
                && (o.AcceptanceEvidenceFileId != null || o.DeclineEvidenceFileId != null))
            .ToListAsync(ct);
        var fileIds = new HashSet<Guid>();
        foreach (var offer in offers)
        {
            if (offer.AcceptanceEvidenceFileId is { } acceptance)
            {
                fileReferenceOutbox.EnqueueRelease(acceptance, OfferEvidenceHolderKey(offer.Id, AcceptanceEvidenceField));
                fileIds.Add(acceptance);
                offer.AcceptanceEvidenceFileId = null;
            }
            if (offer.DeclineEvidenceFileId is { } decline)
            {
                fileReferenceOutbox.EnqueueRelease(decline, OfferEvidenceHolderKey(offer.Id, DeclineEvidenceField));
                fileIds.Add(decline);
                offer.DeclineEvidenceFileId = null;
            }
        }
        StampReleaseTenant(scope.TenantId, fileIds);
        return fileIds.Count;
    }

    /// <summary>
    /// PLAN-30 K1: replaces the candidate free text of the pair's offers (<c>AcceptanceNote</c>, <c>DeclineReason</c>,
    /// <c>RevokeReason</c>) and appeals (<c>Reason</c>, <c>AdditionalInfo</c>) with <see cref="NeutralizedText"/>;
    /// returns how many offer and appeal rows changed.
    /// </summary>
    private async Task<int> NeutralizeOfferAndAppealTextAsync(Scope scope, Guid applicationId, CancellationToken ct)
    {
        var offers = await Query<OfferLetter>(scope)
            .Where(o => o.RecruitmentApplicationId == applicationId
                && ((o.AcceptanceNote != null && o.AcceptanceNote != NeutralizedText)
                    || (o.DeclineReason != null && o.DeclineReason != NeutralizedText)
                    || (o.RevokeReason != null && o.RevokeReason != NeutralizedText)))
            .ToListAsync(ct);
        foreach (var offer in offers)
        {
            offer.AcceptanceNote = Neutralize(offer.AcceptanceNote);
            offer.DeclineReason = Neutralize(offer.DeclineReason);
            offer.RevokeReason = Neutralize(offer.RevokeReason);
        }

        var appeals = await Query<RecruitmentAppeal>(scope)
            .Where(x => x.RecruitmentApplicationId == applicationId
                && (x.Reason != NeutralizedText || (x.AdditionalInfo != null && x.AdditionalInfo != NeutralizedText)))
            .ToListAsync(ct);
        foreach (var appeal in appeals)
        {
            appeal.Reason = NeutralizedText;
            appeal.AdditionalInfo = Neutralize(appeal.AdditionalInfo);
        }
        return offers.Count + appeals.Count;

        static string? Neutralize(string? value) => value is null ? null : NeutralizedText;
    }

    /// <summary>Holder fields of the on-behalf evidence (same values as <c>OfferOnBehalfService</c>).</summary>
    public const string AcceptanceEvidenceField = "AcceptanceEvidence";
    public const string DeclineEvidenceField = "DeclineEvidence";

    /// <summary>Same holder key as the registration (<c>OfferOnBehalfService.HolderKey</c>).</summary>
    public static string OfferEvidenceHolderKey(Guid offerId, string field) =>
        FileReferenceClientExtensions.BuildHolderKey(HrFileServices.HrCore, nameof(OfferLetter), offerId, field);

    /// <summary>Same holder key as the registration (<c>CandidateDocumentService.HolderKey</c>).</summary>
    public static string HolderKey(CandidateDocument document) =>
        FileReferenceClientExtensions.BuildHolderKey(
            HrFileServices.HrCore, nameof(CandidateDocument), document.Id, nameof(CandidateDocument.FileId));

    private async Task<int> NeutralizeNotificationsAsync(Scope scope, Guid applicationId, CancellationToken ct)
    {
        var notifications = await Query<CandidateNotification>(scope)
            .Where(n => n.RecruitmentApplicationId == applicationId
                && (n.Body != NeutralizedText || n.RecipientMasked != NeutralizedText || n.Subject != null))
            .ToListAsync(ct);
        foreach (var notification in notifications)
        {
            notification.Body = NeutralizedText;
            notification.Subject = null;
            notification.RecipientMasked = NeutralizedText;
            if (notification.Status == CandidateNotificationStatus.Pending)
            {
                notification.Status = CandidateNotificationStatus.Suppressed;
                notification.NextAttemptAt = null;
                notification.LastError = SuppressedReason;
            }
        }
        return notifications.Count;
    }

    /// <summary>
    /// One explicit audit row per anonymized pair: counts only. (The generic audit interceptor also logs the changed
    /// columns with their old values — only <c>[AuditRedact]</c> columns are masked; a known GOV-001 issue.)
    /// </summary>
    private void AddAudit(
        Guid tenantId, string entityType, Guid entityId, Guid? actor, string triggeredBy,
        int citizenRows, int internalRows, int documents, int notifications, int otpChallenges, int offerEvidence,
        int offerAndAppealTextRows) =>
        db.AuditLogs.Add(new EntityAuditLog
        {
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            Action = AnonymizedAuditAction,
            UserId = actor,
            ChangedFields = JsonSerializer.Serialize(new
            {
                triggeredBy = triggeredBy == CandidateRetentionRun.JobTrigger ? CandidateRetentionRun.JobTrigger : "User",
                retentionMonths = Months,
                citizenRows,
                internalRows,
                documentsReleased = documents,
                notificationsNeutralized = notifications,
                otpChallengesDeleted = otpChallenges,
                offerEvidenceReleased = offerEvidence,
                offerAndAppealTextNeutralized = offerAndAppealTextRows
            })
        });

    // ------------------------------------------------------------------ queries

    private readonly record struct Scope(Guid TenantId, bool System);

    /// <summary>The job reads across tenants (sanctioned bypass) and restricts to one tenant explicitly.</summary>
    private IQueryable<T> Query<T>(Scope scope) where T : BaseEntity
    {
        IQueryable<T> query = db.Set<T>();
        if (scope.System)
            query = query.IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing);
        return query.Where(e => e.TenantId == scope.TenantId);
    }

    private async Task<List<Guid>> TenantsWithCandidatesAsync(CancellationToken ct)
    {
        var fromApplications = await db.RecruitmentApplications
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Select(a => a.TenantId).Distinct().ToListAsync(ct);
        var fromCitizens = await db.CitizenApplications
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .Select(c => c.TenantId).Distinct().ToListAsync(ct);
        return fromApplications.Concat(fromCitizens).Where(t => t != Guid.Empty).Distinct().OrderBy(t => t).ToList();
    }

    private async Task<CandidateRetentionCounts> CountAsync(Scope scope, DateTime utcNow, CancellationToken ct)
    {
        var cutoff = CandidateApplicationLegalLifecycleService.RetentionCutoff(utcNow, Months);
        var past = PastRetentionPairs(scope, cutoff);
        var held = await Held(scope, past).CountAsync(ct);
        var appeal = await PendingAppeal(scope, NotHeld(scope, past)).CountAsync(ct);
        var due = await DuePairs(scope, cutoff).CountAsync(ct);

        var legacy = PastRetentionLegacy(scope, cutoff);
        var legacyHeld = await legacy.CountAsync(c => c.LegalHold, ct);
        var legacyDue = await legacy.CountAsync(c => !c.LegalHold, ct);
        return new CandidateRetentionCounts(due + legacyDue, held + legacyHeld, appeal);
    }

    /// <summary>
    /// Pairs past retention that still hold data: not hired; requisition closed or cancelled (or none); trigger date on or
    /// before the cutoff; and not fully anonymized — either row not anonymized yet, or something left behind (documents,
    /// messages not neutralized, profile or free text, the internal row's motivation, /careers/my code challenges or
    /// sign-in state, on-behalf offer evidence, offer or appeal free text).
    /// </summary>
    private IQueryable<RecruitmentApplication> PastRetentionPairs(Scope scope, DateTime cutoff)
    {
        var citizens = Query<CitizenApplication>(scope);
        var internals = Query<InternalApplication>(scope);
        var documents = Query<CandidateDocument>(scope);
        var notifications = Query<CandidateNotification>(scope);
        var otpChallenges = Query<CandidateOtpChallenge>(scope);
        var offers = Query<OfferLetter>(scope);
        var appeals = Query<RecruitmentAppeal>(scope);
        var examCandidates = Query<ExamCandidate>(scope);
        var preferences = Query<CompetitionPreference>(scope);
        var liveCompetitionStatuses = CandidateApplicationLegalLifecycleService.CompetitionLiveStatuses.ToList();
        return Query<RecruitmentApplication>(scope)
            .Where(a => a.HiredEmployeeId == null)
            // HCM-031 (PLAN-31): competition applications are triggered by the competition's end only, and never while
            // they still hold a line or a live reserve place.
            .Where(a => (a.CompetitionId != null
                    && !liveCompetitionStatuses.Contains(a.Status)
                    && ((a.Competition!.Status == RecruitmentCompetitionStatus.Finalized && a.Competition.FinalizedAt <= cutoff)
                        || (a.Competition.Status == RecruitmentCompetitionStatus.Cancelled && a.Competition.CancelledAt <= cutoff)))
                || (a.CompetitionId == null && a.RequisitionId == null && a.AppliedAt <= cutoff)
                || (a.CompetitionId == null && a.RequisitionId != null
                    && (a.Requisition!.Status == RequisitionStatus.Closed || a.Requisition.Status == RequisitionStatus.Cancelled)
                    && (a.Requisition.ClosedDate ?? a.Requisition.CancelledAt ?? a.AppliedAt) <= cutoff))
            .Where(a => a.AnonymizedAt == null
                || a.Notes != null || a.AppealNotes != null || a.RejectionNote != null || a.EligibilityOverrideReason != null
                || a.BirthDate != null || a.EducationLevelCode != null || a.Specialization != null
                || a.GraduationYear != null || a.YearsOfExperience != null
                || citizens.Any(c => c.PromotedRecruitmentApplicationId == a.Id
                    && (c.AnonymizedAt == null
                        || c.Phone != null || c.Email != null || c.CoverNote != null
                        || c.NotificationChannel != null || c.NotificationConsentAt != null
                        || c.BirthDate != null || c.EducationLevelCode != null || c.Specialization != null
                        || c.GraduationYear != null || c.YearsOfExperience != null
                        || c.PortalLockedUntil != null || c.PortalOtpRequestedAt != null))
                || internals.Any(i => (i.PromotedRecruitmentApplicationId == a.Id || i.Id == a.SourceInternalApplicationId)
                    && i.Motivation != null)
                || documents.Any(d => d.RecruitmentApplicationId == a.Id)
                || otpChallenges.Any(o => citizens.Any(c => c.PromotedRecruitmentApplicationId == a.Id && c.Id == o.CitizenApplicationId))
                || offers.Any(o => o.RecruitmentApplicationId == a.Id
                    && (o.AcceptanceEvidenceFileId != null || o.DeclineEvidenceFileId != null
                        || (o.AcceptanceNote != null && o.AcceptanceNote != NeutralizedText)
                        || (o.DeclineReason != null && o.DeclineReason != NeutralizedText)
                        || (o.RevokeReason != null && o.RevokeReason != NeutralizedText)))
                || appeals.Any(x => x.RecruitmentApplicationId == a.Id
                    && (x.Reason != NeutralizedText || (x.AdditionalInfo != null && x.AdditionalInfo != NeutralizedText)))
                || notifications.Any(n => n.RecruitmentApplicationId == a.Id
                    && (n.Body != NeutralizedText || n.RecipientMasked != NeutralizedText || n.Subject != null))
                // HCM-031: competition residue (admission card hash, per-line eligibility notes).
                || examCandidates.Any(c => c.RecruitmentApplicationId == a.Id && c.AdmissionCardSha256 != null)
                || preferences.Any(p => p.RecruitmentApplicationId == a.Id && p.EligibilityFlags != null));
    }

    private IQueryable<RecruitmentApplication> Held(Scope scope, IQueryable<RecruitmentApplication> pairs)
    {
        var citizens = Query<CitizenApplication>(scope);
        return pairs.Where(a => a.LegalHold || citizens.Any(c => c.PromotedRecruitmentApplicationId == a.Id && c.LegalHold));
    }

    private IQueryable<RecruitmentApplication> NotHeld(Scope scope, IQueryable<RecruitmentApplication> pairs)
    {
        var citizens = Query<CitizenApplication>(scope);
        return pairs.Where(a => !a.LegalHold && !citizens.Any(c => c.PromotedRecruitmentApplicationId == a.Id && c.LegalHold));
    }

    private IQueryable<RecruitmentApplication> PendingAppeal(Scope scope, IQueryable<RecruitmentApplication> pairs)
    {
        var appeals = Query<RecruitmentAppeal>(scope);
        return pairs.Where(a => a.AppealStatus == RecruitmentAppealStatus.Pending
            || appeals.Any(x => x.RecruitmentApplicationId == a.Id && x.Status == RecruitmentAppealStatus.Pending));
    }

    private IQueryable<RecruitmentApplication> DuePairs(Scope scope, DateTime cutoff)
    {
        var appeals = Query<RecruitmentAppeal>(scope);
        return NotHeld(scope, PastRetentionPairs(scope, cutoff))
            .Where(a => a.AppealStatus != RecruitmentAppealStatus.Pending
                && !appeals.Any(x => x.RecruitmentApplicationId == a.Id && x.Status == RecruitmentAppealStatus.Pending));
    }

    /// <summary>The HCM-006 rule for unlinked citizen rows: trigger = requisition <c>ClosedDate</c> ?? <c>SubmittedAt</c>.</summary>
    private IQueryable<CitizenApplication> PastRetentionLegacy(Scope scope, DateTime cutoff) =>
        Query<CitizenApplication>(scope)
            .Where(c => c.PromotedRecruitmentApplicationId == null && c.AnonymizedAt == null)
            .Where(c => (c.Requisition!.ClosedDate ?? c.SubmittedAt) <= cutoff);

    private IQueryable<CitizenApplication> DueLegacy(Scope scope, DateTime cutoff) =>
        PastRetentionLegacy(scope, cutoff).Where(c => !c.LegalHold);

    private async Task<List<CandidateRetentionDueItem>> LoadPairItemsAsync(
        Scope scope, IQueryable<RecruitmentApplication> page, CancellationToken ct)
    {
        var citizens = Query<CitizenApplication>(scope);
        var rows = await page
            .Select(a => new
            {
                a.Id,
                Reference = citizens.Where(c => c.PromotedRecruitmentApplicationId == a.Id)
                    .Select(c => c.ReferenceNumber).FirstOrDefault(),
                RequisitionNumber = a.Requisition != null ? a.Requisition.RequisitionNumber : null,
                RequisitionStatus = a.Requisition != null ? (RequisitionStatus?)a.Requisition.Status : null,
                ClosedDate = a.Requisition != null ? a.Requisition.ClosedDate : null,
                CancelledAt = a.Requisition != null ? a.Requisition.CancelledAt : null,
                a.AppliedAt,
                Partial = a.AnonymizedAt != null
                    || citizens.Any(c => c.PromotedRecruitmentApplicationId == a.Id && c.AnonymizedAt != null)
            })
            .ToListAsync(ct);
        return rows.Select(r => new CandidateRetentionDueItem(
                r.Id,
                IsLegacyCitizen: false,
                r.Reference ?? ShortReference(r.Id),
                r.RequisitionNumber,
                r.ClosedDate ?? r.CancelledAt ?? r.AppliedAt,
                TriggerKind(r.RequisitionStatus, r.ClosedDate, r.CancelledAt),
                r.Partial))
            .ToList();
    }

    private static async Task<List<CandidateRetentionDueItem>> LoadLegacyItemsAsync(
        IQueryable<CitizenApplication> page, CancellationToken ct)
    {
        var rows = await page
            .Select(c => new
            {
                c.Id,
                c.ReferenceNumber,
                RequisitionNumber = c.Requisition != null ? c.Requisition.RequisitionNumber : null,
                RequisitionStatus = c.Requisition != null ? (RequisitionStatus?)c.Requisition.Status : null,
                ClosedDate = c.Requisition != null ? c.Requisition.ClosedDate : null,
                c.SubmittedAt
            })
            .ToListAsync(ct);
        return rows.Select(r => new CandidateRetentionDueItem(
                r.Id,
                IsLegacyCitizen: true,
                string.IsNullOrEmpty(r.ReferenceNumber) ? ShortReference(r.Id) : r.ReferenceNumber,
                r.RequisitionNumber,
                r.ClosedDate ?? r.SubmittedAt,
                // HCM-006 trigger (ClosedDate ?? SubmittedAt): a cancelled requisition counts through its ClosedDate only.
                r.ClosedDate is not null ? TriggerKind(r.RequisitionStatus, r.ClosedDate, null) : CandidateRetentionTriggerKind.Applied,
                PartiallyAnonymized: false))
            .ToList();
    }

    /// <summary>
    /// Why the trigger date was chosen. A cancellation (<c>RecruitmentRequisitionCascade.CancelAsync</c>) sets both
    /// <c>ClosedDate</c> and <c>CancelledAt</c>, so the requisition status decides, not which date is set.
    /// </summary>
    public static CandidateRetentionTriggerKind TriggerKind(RequisitionStatus? status, DateTime? closedDate, DateTime? cancelledAt) =>
        status == RequisitionStatus.Cancelled && (closedDate ?? cancelledAt) is not null ? CandidateRetentionTriggerKind.RequisitionCancelled
        : closedDate is not null ? CandidateRetentionTriggerKind.RequisitionClosed
        : cancelledAt is not null ? CandidateRetentionTriggerKind.RequisitionCancelled
        : CandidateRetentionTriggerKind.Applied;

    /// <summary>Internal and HR-entered applications have no reference number: the first 8 hex digits of the id.</summary>
    public static string ShortReference(Guid id) => "#" + id.ToString("N")[..8].ToUpperInvariant();

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
