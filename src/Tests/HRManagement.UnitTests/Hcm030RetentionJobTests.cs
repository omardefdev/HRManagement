using System.Data.Common;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Notifications;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-030 (G1–G3): pair-aware candidate retention. "Now" is injected (a fixed date far from today) and no date is parsed
/// or formatted, so the tests do not depend on the clock or the machine culture. The SQLite context carries the tenant
/// stamp interceptor with no ambient tenant, as the background job runs: any row added without an explicit tenant fails.
/// </summary>
public class Hcm030RetentionJobTests
{
    private static readonly DateTime Now = new(2031, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Due = Now.AddMonths(-37);
    private static readonly DateTime NotYet = Now.AddMonths(-35);
    private static readonly Guid TenantB = Guid.Parse("30300000-0000-4000-8000-00000000000b");
    private static readonly Guid AdminId = Guid.Parse("30300000-0000-4000-8000-0000000000ad");

    private const string CitizenName = "سامر المتقدّم";
    private const string CitizenNationalId = "0303030303";
    private const string CitizenEmail = "samer@example.sy";
    private const string CitizenPhone = "+963999123456";

    // ------------------------------------------------------------------ G1: pair rule

    [Fact]
    public async Task Pair_is_due_after_the_retention_months_and_not_before()
    {
        await using var db = CreateContext();
        var due = await SeedPublicPairAsync(db, "D", closedAt: Due);
        await SeedPublicPairAsync(db, "N", closedAt: NotYet);

        var counts = await Service(db).CountAsync(Hcm030TestSupport.TenantA, Now);
        Assert.Equal(new CandidateRetentionCounts(1, 0, 0), counts);

        var (items, total) = await Service(db).GetDuePageAsync(Hcm030TestSupport.TenantA, Now, 1, 25);
        Assert.Equal(1, total);
        var item = Assert.Single(items);
        Assert.Equal(due.App.Id, item.Id);
        Assert.Equal(due.Citizen.ReferenceNumber, item.Reference);
        Assert.Equal(Due, item.TriggerDate);
        Assert.Equal(CandidateRetentionTriggerKind.RequisitionClosed, item.TriggerKind);
        Assert.False(item.IsLegacyCitizen);
        Assert.False(item.PartiallyAnonymized);
    }

    [Fact]
    public async Task Retention_months_come_from_the_option()
    {
        await using var db = CreateContext();
        await SeedPublicPairAsync(db, "M", closedAt: Now.AddMonths(-13));

        Assert.Equal(0, (await Service(db).CountAsync(Hcm030TestSupport.TenantA, Now)).Due);
        Assert.Equal(1, (await Service(db, months: 12).CountAsync(Hcm030TestSupport.TenantA, Now)).Due);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Legal_hold_on_either_row_leaves_the_pair_untouched(bool holdApplication, bool holdCitizen)
    {
        // AC-HCM-30i (negative): a pair under legal hold is never anonymized, whichever row carries the hold.
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "H", closedAt: Due);
        pair.App.LegalHold = holdApplication;
        pair.Citizen.LegalHold = holdCitizen;
        await db.SaveChangesAsync();

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));

        Assert.Equal(0, run.DueCount);
        Assert.Equal(1, run.SkippedLegalHold);
        Assert.Equal(0, run.AnonymizedCount);
        await AssertUntouchedAsync(db, pair);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Pending_appeal_leaves_the_pair_untouched(bool mirrorOnly)
    {
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "A", closedAt: Due);
        if (mirrorOnly)
            pair.App.AppealStatus = RecruitmentAppealStatus.Pending;
        else
            db.RecruitmentAppeals.Add(new RecruitmentAppeal
            {
                TenantId = pair.App.TenantId,
                RecruitmentApplicationId = pair.App.Id,
                Status = RecruitmentAppealStatus.Pending,
                Reason = "تظلّم"
            });
        await db.SaveChangesAsync();

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));

        Assert.Equal(0, run.DueCount);
        Assert.Equal(1, run.SkippedAppeal);
        Assert.Equal(0, run.AnonymizedCount);
        await AssertUntouchedAsync(db, pair);
    }

    [Fact]
    public async Task Open_requisition_never_triggers()
    {
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "O", closedAt: null, appliedAt: Now.AddYears(-10));

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));

        Assert.Equal(0, run.DueCount);
        Assert.Equal(0, run.AnonymizedCount);
        await AssertUntouchedAsync(db, pair);
        Assert.Equal(CandidateRetentionVerdict.OpenRequisition,
            CandidateApplicationLegalLifecycleService.EvaluatePair(pair.App, pair.Requisition, pair.Citizen, false, Now));
    }

    [Fact]
    public async Task Hired_application_is_untouched()
    {
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "E", closedAt: Due);
        var employee = await Hcm030TestSupport.SeedEmployeeAsync(db, "EMP-RET-1");
        pair.App.HiredEmployeeId = employee.Id;
        await db.SaveChangesAsync();

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));

        Assert.Equal(0, run.DueCount);
        await AssertUntouchedAsync(db, pair);
    }

    [Fact]
    public void Trigger_date_is_closed_then_cancelled_then_applied()
    {
        var app = new RecruitmentApplication { AppliedAt = Due.AddDays(-30) };
        var cancelled = new RecruitmentRequisition { Status = RequisitionStatus.Cancelled, CancelledAt = Due };
        var closed = new RecruitmentRequisition { Status = RequisitionStatus.Closed, ClosedDate = Due.AddDays(1), CancelledAt = Due };

        Assert.Equal(Due, CandidateApplicationLegalLifecycleService.RetentionTrigger(app, cancelled));
        Assert.Equal(Due.AddDays(1), CandidateApplicationLegalLifecycleService.RetentionTrigger(app, closed));
        Assert.Equal(app.AppliedAt, CandidateApplicationLegalLifecycleService.RetentionTrigger(app, null));
        Assert.Null(CandidateApplicationLegalLifecycleService.RetentionTrigger(
            app, new RecruitmentRequisition { Status = RequisitionStatus.Published }));
        Assert.Equal(CandidateRetentionVerdict.Due,
            CandidateApplicationLegalLifecycleService.EvaluatePair(app, cancelled, null, false, Now));
        Assert.Equal(CandidateRetentionVerdict.NotYetDue,
            CandidateApplicationLegalLifecycleService.EvaluatePair(
                app, new RecruitmentRequisition { Status = RequisitionStatus.Closed, ClosedDate = NotYet }, null, false, Now));
    }

    // ------------------------------------------------------------------ G3: dry run

    [Fact]
    public async Task Dry_run_changes_nothing_and_logs_a_run()
    {
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "R", closedAt: Due);

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: false)));

        Assert.True(run.DryRun);
        Assert.Equal(1, run.DueCount);
        Assert.Equal(0, run.AnonymizedCount);
        Assert.Equal(Hcm030TestSupport.TenantA, run.TenantId);
        Assert.Equal(CandidateRetentionRun.JobTrigger, run.TriggeredBy);
        Assert.Equal(Now, run.StartedAt);
        Assert.NotNull(run.FinishedAt);
        Assert.Null(run.Error);
        await AssertUntouchedAsync(db, pair);
        Assert.Empty(await db.FileReferenceOutbox.AsNoTracking().Where(o => o.Operation == FileReferenceOutboxOps.Release).ToListAsync());
        var logged = await db.CandidateRetentionRuns.AsNoTracking().SingleAsync();
        Assert.True(logged.DryRun);
        Assert.Equal(1, logged.DueCount);
    }

    [Fact]
    public async Task Apply_requested_while_the_option_is_false_is_a_dry_run()
    {
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "F", closedAt: Due);

        var run = Assert.Single(await Service(db, apply: false).RunAsync(Request(apply: true)));

        Assert.True(run.DryRun);
        Assert.Equal(0, run.AnonymizedCount);
        await AssertUntouchedAsync(db, pair);
    }

    // ------------------------------------------------------------------ G2: apply

    [Fact]
    public async Task Apply_clears_every_listed_field_releases_files_and_neutralizes_notifications()
    {
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "P", closedAt: Due);

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true, triggeredBy: AdminId.ToString())));

        Assert.False(run.DryRun);
        Assert.Equal(1, run.DueCount);
        Assert.Equal(1, run.AnonymizedCount);
        Assert.Null(run.Error);
        Assert.Equal(AdminId.ToString(), run.TriggeredBy);

        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == pair.App.Id);
        Assert.Equal(Now, app.AnonymizedAt);
        Assert.StartsWith("ANON ", app.ApplicantFullName);
        Assert.StartsWith("ANON-", app.NationalId);
        Assert.Equal(PiiMask.Sha256Hex(app.NationalId!), app.NationalIdHash);
        Assert.Null(app.Notes);
        Assert.Null(app.AppealNotes);
        Assert.Null(app.RejectionNote);
        Assert.Null(app.EligibilityOverrideReason);
        Assert.Null(app.BirthDate);
        Assert.Null(app.Specialization);
        Assert.Null(app.GraduationYear);
        Assert.Null(app.YearsOfExperience);
        Assert.Null(app.EducationLevelCode);

        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == pair.Citizen.Id);
        Assert.Equal(Now, citizen.AnonymizedAt);
        Assert.StartsWith("ANON ", citizen.FullName);
        Assert.StartsWith("ANON-", citizen.NationalId);
        Assert.Null(citizen.Phone);
        Assert.Null(citizen.Email);
        Assert.Null(citizen.NotificationChannel);
        Assert.Null(citizen.NotificationConsentAt);
        Assert.Null(citizen.CoverNote);
        Assert.Null(citizen.BirthDate);
        Assert.Null(citizen.Specialization);
        Assert.Null(citizen.GraduationYear);
        Assert.Null(citizen.YearsOfExperience);
        Assert.Null(citizen.EducationLevelCode);
        Assert.Equal(pair.Citizen.ReferenceNumber, citizen.ReferenceNumber);

        // Documents: one release per file, tenant set explicitly (no ambient tenant in the job), rows deleted.
        Assert.Empty(await db.CandidateDocuments.AsNoTracking().ToListAsync());
        var releases = await db.FileReferenceOutbox.AsNoTracking()
            .Where(o => o.Operation == FileReferenceOutboxOps.Release).ToListAsync();
        Assert.Equal(pair.Documents.Select(d => d.FileId).OrderBy(x => x), releases.Select(r => r.FileId).OrderBy(x => x));
        Assert.All(releases, r =>
        {
            Assert.NotEqual(Guid.Empty, r.TenantId);
            Assert.Equal(Hcm030TestSupport.TenantA, r.TenantId);
            Assert.Equal(FileReferenceOutboxStatus.Pending, r.Status);
        });
        Assert.Equal(
            pair.Documents.Select(CandidateRetentionService.HolderKey).OrderBy(x => x, StringComparer.Ordinal),
            releases.Select(r => r.HolderKey).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(pair.Documents, d =>
            Assert.Equal($"hr-core:CandidateDocument:{d.Id:N}:FileId", CandidateRetentionService.HolderKey(d)));

        // Notifications: no rendered text or recipient left; the pending one will never be sent.
        var notifications = await db.CandidateNotifications.AsNoTracking().ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, n =>
        {
            Assert.Equal(CandidateRetentionService.NeutralizedText, n.Body);
            Assert.Equal(CandidateRetentionService.NeutralizedText, n.RecipientMasked);
            Assert.Null(n.Subject);
        });
        Assert.Equal(CandidateNotificationStatus.Sent, notifications.Single(n => n.TemplateCode == "APP_RECEIVED").Status);
        var pending = notifications.Single(n => n.TemplateCode == "OFFER_ISSUED");
        Assert.Equal(CandidateNotificationStatus.Suppressed, pending.Status);
        Assert.Null(pending.NextAttemptAt);

        // One explicit audit row with counts only.
        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == CandidateRetentionService.AnonymizedAuditAction);
        Assert.Equal(pair.App.Id, audit.EntityId);
        Assert.Equal(AdminId, audit.UserId);
        Assert.Equal(Hcm030TestSupport.TenantA, audit.TenantId);
        foreach (var pii in new[] { CitizenName, CitizenNationalId, CitizenEmail, CitizenPhone, "تخصص" })
            Assert.DoesNotContain(pii, audit.ChangedFields);
        using var json = JsonDocument.Parse(audit.ChangedFields!);
        Assert.Equal(2, json.RootElement.GetProperty("documentsReleased").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("notificationsNeutralized").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("otpChallengesDeleted").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("offerEvidenceReleased").GetInt32());

        // Fully anonymized: the next run finds nothing.
        var next = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));
        Assert.Equal(0, next.DueCount);
        Assert.Equal(0, next.AnonymizedCount);
        Assert.Equal(2, await db.CandidateRetentionRuns.CountAsync());
    }

    [Fact]
    public async Task Apply_deletes_otp_challenges_clears_the_portal_lock_and_releases_offer_evidence()
    {
        // PLAN-30 K1: the pair's /careers/my codes and sign-in state go; the on-behalf evidence files are released with the
        // pair's tenant and their ids cleared. Another pair's challenge stays.
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "OTP", closedAt: Due);
        var other = await SeedPublicPairAsync(db, "KEEP", closedAt: NotYet);
        pair.Citizen.PortalLockedUntil = Now.AddMinutes(10);
        pair.Citizen.PortalOtpRequestedAt = Now.AddMinutes(-5);
        db.CandidateOtpChallenges.AddRange(
            NewOtpChallenge(pair.Citizen, consumed: true),
            NewOtpChallenge(pair.Citizen, consumed: false),
            NewOtpChallenge(other.Citizen, consumed: false));
        var accepted = NewOffer(pair, "OFF-RET-1", OfferStatus.Accepted);
        accepted.AcceptedBy = OfferAcceptedBy.HrOnBehalf;
        accepted.AcceptanceEvidenceFileId = Guid.NewGuid();
        var declined = NewOffer(pair, "OFF-RET-2", OfferStatus.Declined);
        declined.DeclinedBy = OfferDeclinedBy.HrOnBehalf;
        declined.DeclineEvidenceFileId = Guid.NewGuid();
        db.OfferLetters.AddRange(accepted, declined);
        await db.SaveChangesAsync();
        var acceptanceFile = accepted.AcceptanceEvidenceFileId.Value;
        var declineFile = declined.DeclineEvidenceFileId.Value;

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));
        Assert.Equal(1, run.AnonymizedCount);

        var challenges = await db.CandidateOtpChallenges.AsNoTracking().ToListAsync();
        var left = Assert.Single(challenges);
        Assert.Equal(other.Citizen.Id, left.CitizenApplicationId);
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == pair.Citizen.Id);
        Assert.Null(citizen.PortalLockedUntil);
        Assert.Null(citizen.PortalOtpRequestedAt);

        var offers = await db.OfferLetters.AsNoTracking().Where(o => o.RecruitmentApplicationId == pair.App.Id).ToListAsync();
        Assert.All(offers, o =>
        {
            Assert.Null(o.AcceptanceEvidenceFileId);
            Assert.Null(o.DeclineEvidenceFileId);
        });
        var releases = await db.FileReferenceOutbox.AsNoTracking()
            .Where(o => o.Operation == FileReferenceOutboxOps.Release && (o.FileId == acceptanceFile || o.FileId == declineFile))
            .ToListAsync();
        Assert.Equal(2, releases.Count);
        Assert.All(releases, r => Assert.Equal(Hcm030TestSupport.TenantA, r.TenantId));
        Assert.Equal($"hr-core:OfferLetter:{accepted.Id:N}:AcceptanceEvidence", releases.Single(r => r.FileId == acceptanceFile).HolderKey);
        Assert.Equal($"hr-core:OfferLetter:{declined.Id:N}:DeclineEvidence", releases.Single(r => r.FileId == declineFile).HolderKey);

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == CandidateRetentionService.AnonymizedAuditAction);
        using var json = JsonDocument.Parse(audit.ChangedFields!);
        Assert.Equal(2, json.RootElement.GetProperty("otpChallengesDeleted").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("offerEvidenceReleased").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("documentsReleased").GetInt32());
    }

    [Fact]
    public async Task Pair_anonymized_with_otp_residue_or_offer_evidence_left_is_due_again()
    {
        // Rows anonymized before PLAN-30 K1 kept their code challenges, lock and evidence: the pair is picked up again.
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "RES", closedAt: Due);
        Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));
        Assert.Equal(0, (await Service(db).CountAsync(Hcm030TestSupport.TenantA, Now)).Due);

        db.CandidateOtpChallenges.Add(NewOtpChallenge(pair.Citizen, consumed: true));
        await db.SaveChangesAsync();
        Assert.Equal(1, (await Service(db).CountAsync(Hcm030TestSupport.TenantA, Now)).Due);
        Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));
        Assert.Empty(await db.CandidateOtpChallenges.AsNoTracking().ToListAsync());

        var citizen = await db.CitizenApplications.SingleAsync(c => c.Id == pair.Citizen.Id);
        citizen.PortalLockedUntil = Now;
        await db.SaveChangesAsync();
        Assert.Equal(1, (await Service(db).CountAsync(Hcm030TestSupport.TenantA, Now)).Due);
        Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));

        var offer = NewOffer(pair, "OFF-RES-1", OfferStatus.Declined);
        offer.DeclineEvidenceFileId = Guid.NewGuid();
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();
        Assert.Equal(1, (await Service(db).CountAsync(Hcm030TestSupport.TenantA, Now)).Due);
        Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));
        Assert.Equal(0, (await Service(db).CountAsync(Hcm030TestSupport.TenantA, Now)).Due);
        Assert.Null((await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == offer.Id)).DeclineEvidenceFileId);
    }

    [Fact]
    public async Task Apply_neutralizes_offer_and_appeal_free_text_and_a_pair_with_text_left_is_due_again()
    {
        // PLAN-30 K1: the /careers/my decline/withdraw/appeal text and the on-behalf note are candidate data; statuses,
        // channels and HR's decision stay. A pair anonymized with such text left is picked up again.
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "TXT", closedAt: Due);
        var declined = NewOffer(pair, "OFF-TXT-1", OfferStatus.Declined);
        declined.DeclinedBy = OfferDeclinedBy.Candidate;
        declined.DeclineReason = "أعتذر لظروف صحية، للتواصل 0999123456";
        var revoked = NewOffer(pair, "OFF-TXT-2", OfferStatus.Revoked);
        revoked.RevokeReason = "انسحاب المرشح: ظروف عائلية";
        var accepted = NewOffer(pair, "OFF-TXT-3", OfferStatus.Accepted);
        accepted.AcceptedBy = OfferAcceptedBy.HrOnBehalf;
        accepted.AcceptanceNote = "قبول هاتفي موثّق من المرشح بتاريخ المقابلة";
        var untouchedOffer = NewOffer(pair, "OFF-TXT-4", OfferStatus.Issued);
        db.OfferLetters.AddRange(declined, revoked, accepted, untouchedOffer);
        var appeal = new RecruitmentAppeal
        {
            TenantId = pair.App.TenantId,
            RecruitmentApplicationId = pair.App.Id,
            Status = RecruitmentAppealStatus.Decided,
            FiledBy = RecruitmentAppealFiledBy.Candidate,
            Reason = "أعترض على القرار لظروفي الشخصية",
            InfoRequest = "يرجى إرفاق ما يثبت الخبرة",
            AdditionalInfo = "عملت لدى جهة خاصة خمس سنوات",
            Decision = "رُفض الاستئناف"
        };
        db.RecruitmentAppeals.Add(appeal);
        await db.SaveChangesAsync();

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));
        Assert.Equal(1, run.AnonymizedCount);

        var offers = await db.OfferLetters.AsNoTracking().ToDictionaryAsync(o => o.Id);
        Assert.Equal(CandidateRetentionService.NeutralizedText, offers[declined.Id].DeclineReason);
        Assert.Equal(OfferDeclinedBy.Candidate, offers[declined.Id].DeclinedBy);
        Assert.Equal(OfferStatus.Declined, offers[declined.Id].Status);
        Assert.Equal(CandidateRetentionService.NeutralizedText, offers[revoked.Id].RevokeReason);
        Assert.Equal(CandidateRetentionService.NeutralizedText, offers[accepted.Id].AcceptanceNote);
        Assert.Equal(OfferAcceptedBy.HrOnBehalf, offers[accepted.Id].AcceptedBy);
        Assert.Null(offers[accepted.Id].DeclineReason);
        Assert.Null(offers[untouchedOffer.Id].AcceptanceNote);
        Assert.Null(offers[untouchedOffer.Id].DeclineReason);
        Assert.Null(offers[untouchedOffer.Id].RevokeReason);

        var storedAppeal = await db.RecruitmentAppeals.AsNoTracking().SingleAsync(x => x.Id == appeal.Id);
        Assert.Equal(CandidateRetentionService.NeutralizedText, storedAppeal.Reason);
        Assert.Equal(CandidateRetentionService.NeutralizedText, storedAppeal.AdditionalInfo);
        Assert.Equal(RecruitmentAppealStatus.Decided, storedAppeal.Status);
        Assert.Equal(RecruitmentAppealFiledBy.Candidate, storedAppeal.FiledBy);
        Assert.Equal("رُفض الاستئناف", storedAppeal.Decision);

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == CandidateRetentionService.AnonymizedAuditAction);
        Assert.DoesNotContain("0999123456", audit.ChangedFields);
        using (var json = JsonDocument.Parse(audit.ChangedFields!))
            Assert.Equal(4, json.RootElement.GetProperty("offerAndAppealTextNeutralized").GetInt32());
        Assert.Equal(0, (await Service(db).CountAsync(Hcm030TestSupport.TenantA, Now)).Due);

        // Text written after the anonymization (e.g. a late decline note) makes the pair due again.
        var late = await db.OfferLetters.SingleAsync(o => o.Id == untouchedOffer.Id);
        late.DeclineReason = "سبب متأخر";
        await db.SaveChangesAsync();
        Assert.Equal(1, (await Service(db).CountAsync(Hcm030TestSupport.TenantA, Now)).Due);
        Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));
        Assert.Equal(CandidateRetentionService.NeutralizedText,
            (await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == untouchedOffer.Id)).DeclineReason);
        Assert.Equal(0, (await Service(db).CountAsync(Hcm030TestSupport.TenantA, Now)).Due);
    }

    [Fact]
    public async Task Internal_pair_anonymizes_the_application_and_clears_the_motivation()
    {
        await using var db = CreateContext();
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db, "INT");
        Close(fx.Requisition, Due);
        var employee = await Hcm030TestSupport.SeedEmployeeAsync(db, "EMP-RET-INT");
        var app = NewApplication(fx.Requisition, RecruitmentIntakeChannel.Internal, Due.AddDays(-20));
        db.RecruitmentApplications.Add(app);
        var internalRow = new InternalApplication
        {
            TenantId = Hcm030TestSupport.TenantA,
            InternalVacancyId = fx.Vacancy.Id,
            EmployeeId = employee.Id,
            RequisitionId = fx.Requisition.Id,
            PromotedRecruitmentApplicationId = app.Id,
            Motivation = "أرغب بالانتقال لأسباب عائلية"
        };
        db.InternalApplications.Add(internalRow);
        await db.SaveChangesAsync();
        app.SourceInternalApplicationId = internalRow.Id; // both links, as unified intake sets them
        await db.SaveChangesAsync();

        var (items, _) = await Service(db).GetDuePageAsync(Hcm030TestSupport.TenantA, Now, 1, 25);
        Assert.Equal(CandidateRetentionService.ShortReference(app.Id), Assert.Single(items).Reference);

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));

        Assert.Equal(1, run.AnonymizedCount);
        var stored = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == app.Id);
        Assert.Equal(Now, stored.AnonymizedAt);
        Assert.Null(stored.BirthDate);
        Assert.Null((await db.InternalApplications.AsNoTracking().SingleAsync(i => i.Id == internalRow.Id)).Motivation);
        Assert.NotNull(await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id));
    }

    [Fact]
    public async Task Half_anonymized_pair_is_completed()
    {
        // A DSAR erasure anonymizes the citizen row only; the recruitment row still holds the name and national id.
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "HA", closedAt: Due);
        Assert.True(CandidateApplicationLegalLifecycleService.TryAnonymize(pair.Citizen, Now.AddMonths(-1)).Success);
        await db.SaveChangesAsync();

        var (items, _) = await Service(db).GetDuePageAsync(Hcm030TestSupport.TenantA, Now, 1, 25);
        Assert.True(Assert.Single(items).PartiallyAnonymized);

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));

        Assert.Equal(1, run.DueCount);
        Assert.Equal(1, run.AnonymizedCount);
        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == pair.App.Id);
        Assert.StartsWith("ANON ", app.ApplicantFullName);
        Assert.Null(app.BirthDate);
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == pair.Citizen.Id);
        Assert.Equal(Now.AddMonths(-1), citizen.AnonymizedAt);
        Assert.Empty(await db.CandidateDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Pair_anonymized_before_hcm030_with_documents_left_is_completed()
    {
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "OLD", closedAt: Due);
        pair.App.AnonymizedAt = Now.AddYears(-1);
        pair.App.ApplicantFullName = "ANON X";
        pair.Citizen.AnonymizedAt = Now.AddYears(-1);
        await db.SaveChangesAsync();

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));

        Assert.Equal(1, run.AnonymizedCount);
        Assert.Empty(await db.CandidateDocuments.AsNoTracking().ToListAsync());
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == pair.Citizen.Id);
        Assert.Null(citizen.Email);
        Assert.Equal(Now.AddYears(-1), citizen.AnonymizedAt);
    }

    [Fact]
    public async Task Admin_run_touches_only_its_tenant_and_the_job_logs_one_run_per_tenant()
    {
        await using var db = CreateContext();
        var pairA = await SeedPublicPairAsync(db, "TA", closedAt: Due);
        var appB = NewApplication(null, RecruitmentIntakeChannel.HrEntered, Due);
        appB.TenantId = TenantB;
        db.RecruitmentApplications.Add(appB);
        await db.SaveChangesAsync();

        var adminRuns = await Service(db, apply: true)
            .RunAsync(Request(apply: true, triggeredBy: AdminId.ToString(), tenantId: TenantB));
        var adminRun = Assert.Single(adminRuns);
        Assert.Equal(TenantB, adminRun.TenantId);
        Assert.Equal(1, adminRun.AnonymizedCount);
        await AssertUntouchedAsync(db, pairA);

        var jobRuns = await Service(db, apply: false).RunAsync(Request(apply: false));
        Assert.Equal(2, jobRuns.Count);
        Assert.Equal(1, jobRuns.Single(r => r.TenantId == Hcm030TestSupport.TenantA).DueCount);
        Assert.Equal(0, jobRuns.Single(r => r.TenantId == TenantB).DueCount);
        Assert.All(jobRuns, r => Assert.NotNull(r.FinishedAt));
    }

    [Fact]
    public async Task Job_skips_only_tenants_with_a_finished_job_run_within_the_interval()
    {
        // A cycle cut short (restart, cancellation, failure) must resume with the tenants it did not finish.
        await using var db = CreateContext();
        await SeedPublicPairAsync(db, "GA", closedAt: Due);
        var appB = NewApplication(null, RecruitmentIntakeChannel.HrEntered, Due);
        appB.TenantId = TenantB;
        db.RecruitmentApplications.Add(appB);
        var tenantC = Guid.Parse("30300000-0000-4000-8000-00000000000c");
        var appC = NewApplication(null, RecruitmentIntakeChannel.HrEntered, Due);
        appC.TenantId = tenantC;
        db.RecruitmentApplications.Add(appC);
        db.CandidateRetentionRuns.AddRange(
            // A: finished a day ago (skipped). B: cut short a day ago (no FinishedAt: runs again).
            // C: finished, but by an admin (manual runs never count for the job).
            new CandidateRetentionRun { TenantId = Hcm030TestSupport.TenantA, StartedAt = Now.AddDays(-1), FinishedAt = Now.AddDays(-1), DryRun = true },
            new CandidateRetentionRun { TenantId = TenantB, StartedAt = Now.AddDays(-1), FinishedAt = null, DryRun = false, Error = "1 failure(s): cancelled" },
            new CandidateRetentionRun { TenantId = tenantC, StartedAt = Now.AddDays(-1), FinishedAt = Now.AddDays(-1), DryRun = true, TriggeredBy = AdminId.ToString() });
        await db.SaveChangesAsync();
        var interval = TimeSpan.FromDays(7);

        var runs = await Service(db, apply: true).RunAsync(Request(apply: true) with { SkipTenantsCompletedWithin = interval });

        Assert.Equal([TenantB, tenantC], runs.Select(r => r.TenantId).OrderBy(t => t));
        Assert.All(runs, r => Assert.Equal(1, r.AnonymizedCount));

        // Once every tenant has finished, a restart within the interval does nothing; after the interval it runs again.
        Assert.Empty(await Service(db, apply: true).RunAsync(Request(apply: true) with { SkipTenantsCompletedWithin = interval }));
        var later = await Service(db).RunAsync(
            new CandidateRetentionRunRequest(Now.AddDays(8), Apply: false, CandidateRetentionRun.JobTrigger, SkipTenantsCompletedWithin: interval));
        Assert.Equal(3, later.Count);
    }

    [Fact]
    public async Task Cancelled_apply_logs_the_pairs_it_committed_and_is_resumed_by_the_next_check()
    {
        using var cts = new CancellationTokenSource();
        await using var db = CreateContext(new CancelAfterFirstCommit(cts));
        await SeedPublicPairAsync(db, "C1", closedAt: Due);
        await SeedPublicPairAsync(db, "C2", closedAt: Due);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service(db, apply: true).RunAsync(Request(apply: true), cts.Token));

        var logged = await db.CandidateRetentionRuns.AsNoTracking().SingleAsync();
        Assert.Equal(2, logged.DueCount);
        Assert.Equal(1, logged.AnonymizedCount);
        Assert.Null(logged.FinishedAt);
        Assert.Contains("cancelled", logged.Error);
        Assert.Equal(1, await db.RecruitmentApplications.AsNoTracking().CountAsync(a => a.AnonymizedAt != null));

        var resumed = Assert.Single(await Service(db, apply: true)
            .RunAsync(Request(apply: true) with { SkipTenantsCompletedWithin = TimeSpan.FromDays(7) }));
        Assert.Equal(1, resumed.AnonymizedCount);
        Assert.NotNull(resumed.FinishedAt);
        Assert.Equal(2, await db.RecruitmentApplications.AsNoTracking().CountAsync(a => a.AnonymizedAt != null));
    }

    [Fact]
    public async Task Audit_names_the_user_for_a_manual_run_whose_user_id_is_not_a_guid()
    {
        await using var db = CreateContext();
        await SeedPublicPairAsync(db, "U", closedAt: Due);

        Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true, triggeredBy: "User")));

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == CandidateRetentionService.AnonymizedAuditAction);
        Assert.Null(audit.UserId);
        using var json = JsonDocument.Parse(audit.ChangedFields!);
        Assert.Equal("User", json.RootElement.GetProperty("triggeredBy").GetString());
    }

    [Fact]
    public async Task Cancelled_requisition_is_labelled_as_cancelled_when_the_cascade_sets_both_dates()
    {
        // RecruitmentRequisitionCascade.CancelAsync sets Status = Cancelled, ClosedDate and CancelledAt together.
        await using var db = CreateContext();
        var pair = await SeedPublicPairAsync(db, "CX", closedAt: null, appliedAt: Due.AddDays(-30));
        pair.Requisition.Status = RequisitionStatus.Cancelled;
        pair.Requisition.ClosedDate = Due;
        pair.Requisition.CancelledAt = Due;
        await db.SaveChangesAsync();

        var (items, _) = await Service(db).GetDuePageAsync(Hcm030TestSupport.TenantA, Now, 1, 25);

        var item = Assert.Single(items);
        Assert.Equal(Due, item.TriggerDate);
        Assert.Equal(CandidateRetentionTriggerKind.RequisitionCancelled, item.TriggerKind);
        Assert.Equal(CandidateRetentionTriggerKind.RequisitionClosed,
            CandidateRetentionService.TriggerKind(RequisitionStatus.Closed, Due, null));
        Assert.Equal(CandidateRetentionTriggerKind.RequisitionCancelled,
            CandidateRetentionService.TriggerKind(RequisitionStatus.Cancelled, null, Due));
        Assert.Equal(CandidateRetentionTriggerKind.Applied, CandidateRetentionService.TriggerKind(null, null, null));
    }

    // ------------------------------------------------------------------ legacy citizen rows

    [Fact]
    public async Task Legacy_unlinked_citizen_rows_keep_the_hcm006_rule()
    {
        await using var db = CreateContext();
        var posting = new PublicJobPosting { TenantId = Hcm030TestSupport.TenantA, Title = "إعلان قديم", IsPublished = true };
        db.PublicJobPostings.Add(posting);
        var due = NewLegacyCitizen(posting, "L-1", submittedAt: Due);
        var recent = NewLegacyCitizen(posting, "L-2", submittedAt: NotYet);
        var held = NewLegacyCitizen(posting, "L-3", submittedAt: Due);
        held.LegalHold = true;
        due.PortalLockedUntil = Now.AddMinutes(10);
        due.PortalOtpRequestedAt = Now.AddMinutes(-5);
        db.CitizenApplications.AddRange(due, recent, held);
        db.CandidateOtpChallenges.AddRange(NewOtpChallenge(due, consumed: false), NewOtpChallenge(held, consumed: false));
        await db.SaveChangesAsync();

        Assert.True(CandidateApplicationLegalLifecycleService.CanPurge(due, null, Now));
        Assert.False(CandidateApplicationLegalLifecycleService.CanPurge(recent, null, Now));
        Assert.False(CandidateApplicationLegalLifecycleService.CanPurge(held, null, Now));
        // Open requisition: the legacy rule falls back to SubmittedAt (unchanged).
        Assert.True(CandidateApplicationLegalLifecycleService.CanPurge(
            due, new RecruitmentRequisition { Status = RequisitionStatus.Published }, Now));
        Assert.False(CandidateApplicationLegalLifecycleService.CanPurge(
            new CitizenApplication { SubmittedAt = Due, PromotedRecruitmentApplicationId = Guid.NewGuid() }, null, Now));

        var (items, _) = await Service(db).GetDuePageAsync(Hcm030TestSupport.TenantA, Now, 1, 25);
        var item = Assert.Single(items);
        Assert.True(item.IsLegacyCitizen);
        Assert.Equal("L-1", item.Reference);

        var run = Assert.Single(await Service(db, apply: true).RunAsync(Request(apply: true)));

        Assert.Equal(1, run.DueCount);
        Assert.Equal(1, run.SkippedLegalHold);
        Assert.Equal(1, run.AnonymizedCount);
        var stored = await db.CitizenApplications.AsNoTracking().ToDictionaryAsync(c => c.ReferenceNumber);
        Assert.Equal(Now, stored["L-1"].AnonymizedAt);
        Assert.Null(stored["L-1"].Email);
        Assert.Null(stored["L-2"].AnonymizedAt);
        Assert.Null(stored["L-3"].AnonymizedAt);
        Assert.Equal(CitizenEmail, stored["L-3"].Email);
        // PLAN-30 K1: the anonymized row's code challenges and sign-in state go; the held row keeps its challenge.
        Assert.Null(stored["L-1"].PortalLockedUntil);
        Assert.Null(stored["L-1"].PortalOtpRequestedAt);
        var challenge = Assert.Single(await db.CandidateOtpChallenges.AsNoTracking().ToListAsync());
        Assert.Equal(held.Id, challenge.CitizenApplicationId);
        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == CandidateRetentionService.AnonymizedAuditAction);
        using var json = JsonDocument.Parse(audit.ChangedFields!);
        Assert.Equal(1, json.RootElement.GetProperty("otpChallengesDeleted").GetInt32());
    }

    [Fact]
    public void TryAnonymize_refuses_a_held_row_and_is_idempotent()
    {
        var citizen = new CitizenApplication { FullName = CitizenName, NationalId = CitizenNationalId, LegalHold = true };
        Assert.False(CandidateApplicationLegalLifecycleService.TryAnonymize(citizen, Now).Success);
        Assert.Equal(CitizenName, citizen.FullName);

        citizen.LegalHold = false;
        Assert.True(CandidateApplicationLegalLifecycleService.TryAnonymize(citizen, Now).Success);
        var name = citizen.FullName;
        citizen.Email = CitizenEmail; // left behind by an older anonymization
        Assert.True(CandidateApplicationLegalLifecycleService.TryAnonymize(citizen, Now.AddDays(5)).Success);
        Assert.Equal(Now, citizen.AnonymizedAt);
        Assert.Equal(name, citizen.FullName);
        Assert.Null(citizen.Email);
    }

    // ------------------------------------------------------------------ helpers

    internal sealed record Pair(
        RecruitmentRequisition Requisition,
        RecruitmentApplication App,
        CitizenApplication Citizen,
        IReadOnlyList<CandidateDocument> Documents);

    internal static HRCoreDbContext CreateContext(params IInterceptor[] extraInterceptors)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new TenantIdStampInterceptor(new NoTenant()))
            .AddInterceptors(extraInterceptors)
            .Options;
        var db = new HRCoreDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>Cancels the run right after the first explicit pair transaction commits (seeding saves do not count).</summary>
    private sealed class CancelAfterFirstCommit(CancellationTokenSource cts) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is HRCoreDbContext db
                && db.ChangeTracker.Entries<EntityAuditLog>().Any(e => e.Entity.Action == CandidateRetentionService.AnonymizedAuditAction))
                cts.Cancel();
            return Task.CompletedTask;
        }
    }

    internal static CandidateRetentionService Service(HRCoreDbContext db, bool apply = false, int months = 36) =>
        new(db,
            new EfFileReferenceOutbox(db),
            Options.Create(new RecruitmentOptions { Retention = new RecruitmentRetentionOptions { Apply = apply, Months = months } }),
            NullLogger<CandidateRetentionService>.Instance);

    private static CandidateRetentionRunRequest Request(bool apply, string? triggeredBy = null, Guid? tenantId = null) =>
        new(Now, apply, triggeredBy ?? CandidateRetentionRun.JobTrigger, tenantId);

    private static void Close(RecruitmentRequisition requisition, DateTime? closedAt)
    {
        if (closedAt is null)
            return;
        requisition.Status = RequisitionStatus.Closed;
        requisition.ClosedDate = closedAt;
    }

    internal static RecruitmentApplication NewApplication(
        RecruitmentRequisition? requisition, RecruitmentIntakeChannel channel, DateTime appliedAt) => new()
        {
            TenantId = Hcm030TestSupport.TenantA,
            ApplicantFullName = CitizenName,
            NationalId = CitizenNationalId,
            NationalIdHash = PiiMask.Sha256Hex(CitizenNationalId),
            AppliedPosition = requisition?.JobTitle ?? "محلل",
            RequisitionId = requisition?.Id,
            IntakeChannel = channel,
            AppliedAt = appliedAt,
            Status = RecruitmentApplicationStatus.Rejected,
            Notes = "ملاحظة الموارد البشرية",
            AppealNotes = "ملاحظة استئناف",
            RejectionNote = "لم يستوفِ شرط الخبرة",
            EligibilityOverrideReason = "قرار اللجنة",
            EducationLevelCode = "BACH",
            Specialization = "تخصص هندسة",
            GraduationYear = 2015,
            YearsOfExperience = 6m,
            BirthDate = new DateOnly(1992, 3, 4)
        };

    private static CitizenApplication NewLegacyCitizen(PublicJobPosting posting, string reference, DateTime submittedAt) => new()
    {
        TenantId = Hcm030TestSupport.TenantA,
        PublicJobPostingId = posting.Id,
        ReferenceNumber = reference,
        FullName = CitizenName,
        NationalId = CitizenNationalId + reference[^1],
        Phone = CitizenPhone,
        Email = CitizenEmail,
        CoverNote = "نبذة",
        SubmittedAt = submittedAt,
        BirthDate = new DateOnly(1992, 3, 4)
    };

    /// <summary>A public pair (citizen row + recruitment application) with two documents and two messages.</summary>
    internal static async Task<Pair> SeedPublicPairAsync(
        HRCoreDbContext db, string suffix, DateTime? closedAt, DateTime? appliedAt = null)
    {
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db, suffix);
        Close(fx.Requisition, closedAt);
        var app = NewApplication(fx.Requisition, RecruitmentIntakeChannel.Public, appliedAt ?? (closedAt ?? Now).AddDays(-40));
        db.RecruitmentApplications.Add(app);
        var citizen = new CitizenApplication
        {
            TenantId = Hcm030TestSupport.TenantA,
            PublicJobPostingId = fx.Posting.Id,
            RequisitionId = fx.Requisition.Id,
            PromotedRecruitmentApplicationId = app.Id,
            ReferenceNumber = "CA-RET-" + suffix,
            FullName = CitizenName,
            NationalId = CitizenNationalId,
            NationalIdHash = PiiMask.Sha256Hex(CitizenNationalId),
            Phone = CitizenPhone,
            Email = CitizenEmail,
            NotificationChannel = NotificationChannel.Email,
            NotificationConsentAt = app.AppliedAt,
            CoverNote = "نبذة عن المتقدّم",
            SubmittedAt = app.AppliedAt,
            EducationLevelCode = "BACH",
            Specialization = "تخصص هندسة",
            GraduationYear = 2015,
            YearsOfExperience = 6m,
            BirthDate = new DateOnly(1992, 3, 4)
        };
        db.CitizenApplications.Add(citizen);
        var documents = new[] { "CV", "Certificate" }.Select(type => new CandidateDocument
        {
            TenantId = Hcm030TestSupport.TenantA,
            RecruitmentApplicationId = app.Id,
            CitizenApplicationId = citizen.Id,
            DocumentType = type,
            FileId = Guid.NewGuid(),
            OriginalFileName = $"{CitizenName}-{type}.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            UploadedAt = app.AppliedAt
        }).ToList();
        db.CandidateDocuments.AddRange(documents);
        db.CandidateNotifications.AddRange(
            NewNotification(app, "APP_RECEIVED", CandidateNotificationStatus.Sent),
            NewNotification(app, "OFFER_ISSUED", CandidateNotificationStatus.Pending));
        await db.SaveChangesAsync();
        return new Pair(fx.Requisition, app, citizen, documents);
    }

    private static CandidateOtpChallenge NewOtpChallenge(CitizenApplication citizen, bool consumed) => new()
    {
        TenantId = citizen.TenantId,
        CitizenApplicationId = citizen.Id,
        CodeHmac = new string('a', 64),
        Channel = NotificationChannel.Email,
        ExpiresAt = Now.AddMinutes(10),
        Attempts = 1,
        ConsumedAt = consumed ? Now.AddMinutes(-1) : null,
        RequestIp = new string('b', 64)
    };

    private static OfferLetter NewOffer(Pair pair, string number, OfferStatus status) => new()
    {
        TenantId = pair.App.TenantId,
        RecruitmentApplicationId = pair.App.Id,
        FundedPositionId = pair.Requisition.FundedPositionId,
        OfferNumber = number,
        Status = status,
        ProposedStartDate = Due
    };

    private static CandidateNotification NewNotification(
        RecruitmentApplication app, string template, CandidateNotificationStatus status) => new()
        {
            TenantId = app.TenantId,
            RecruitmentApplicationId = app.Id,
            EventType = RecruitmentApplicationEventTypes.StatusChanged,
            Channel = NotificationChannel.Email,
            TemplateCode = template,
            RecipientMasked = PiiMask.Email(CitizenEmail),
            Subject = "بوابة التوظيف — " + template,
            Body = "نص الرسالة " + template,
            IdempotencyKey = $"{Guid.NewGuid():D}:Email",
            Status = status,
            NextAttemptAt = status == CandidateNotificationStatus.Pending ? app.AppliedAt : null
        };

    private static async Task AssertUntouchedAsync(HRCoreDbContext db, Pair pair)
    {
        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == pair.App.Id);
        Assert.Null(app.AnonymizedAt);
        Assert.Equal(CitizenName, app.ApplicantFullName);
        Assert.Equal(CitizenNationalId, app.NationalId);
        Assert.NotNull(app.BirthDate);
        Assert.NotNull(app.RejectionNote);
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync(c => c.Id == pair.Citizen.Id);
        Assert.Null(citizen.AnonymizedAt);
        Assert.Equal(CitizenEmail, citizen.Email);
        Assert.NotNull(citizen.NotificationConsentAt);
        Assert.Equal(pair.Documents.Count,
            await db.CandidateDocuments.AsNoTracking().CountAsync(d => d.RecruitmentApplicationId == pair.App.Id));
        Assert.All(await db.CandidateNotifications.AsNoTracking().Where(n => n.RecruitmentApplicationId == pair.App.Id).ToListAsync(),
            n => Assert.NotEqual(CandidateRetentionService.NeutralizedText, n.Body));
        Assert.Empty(await db.FileReferenceOutbox.AsNoTracking()
            .Where(o => o.Operation == FileReferenceOutboxOps.Release
                && pair.Documents.Select(d => d.FileId).Contains(o.FileId))
            .ToListAsync());
    }

    /// <summary>The background job has no ambient tenant.</summary>
    private sealed class NoTenant : ITenantContext
    {
        public Guid? TenantId => null;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => throw new InvalidOperationException("No tenant.");
    }
}
