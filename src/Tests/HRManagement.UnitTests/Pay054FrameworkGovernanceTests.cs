using System.Text.Json;
using HRManagement.ServiceDefaults.LegalControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payroll.Application.Features.PayrollConfigurations;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure;
using H = HRManagement.UnitTests.Pay054FrameworkHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-54 (§3, P-8, P-10): the cycle of a framework version. Four acts and three people: an editor never approves, attests or
/// publishes; the attestor is neither an editor nor the approver; the approver or the attestor publishes, from <c>Approved</c> only
/// and with a matching attestation. Every refusal is an Arabic message and leaves the version, its attestations and every other
/// version as they were. And the interim gate: an interim value is approved, attested, published and calculated only while the
/// STR-002 exception it cites is active on the payroll calendar's day (Damascus), on both sides of 31 January 2027.
/// </summary>
public sealed class Pay054FrameworkGovernanceTests
{
    private static readonly Guid Stranger = Guid.Parse("54000000-0000-4000-8000-0000000000c9");

    private static async Task<(H Harness, Guid Id)> DraftAsync(string? json = null, DateOnly? effectiveFrom = null)
    {
        var h = new H();
        var imported = await h.ImportAsync(json, effectiveFrom: effectiveFrom ?? new DateOnly(2026, 1, 1));
        Assert.True(imported.Success, imported.Error + string.Join(" | ", imported.Errors));
        return (h, imported.FrameworkId!.Value);
    }

    private static async Task AssertRefusedAsync(H h, Guid id, Func<Task<PayrollFrameworkResult>> act, string expectedError, PayrollConfigPublicationStatus status)
    {
        var before = await h.CycleStateAsync(id);

        var result = await act();

        Assert.False(result.Success);
        Assert.Equal(PayrollFrameworkFailure.Invalid, result.Failure);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(expectedError, result.Error);
        Assert.Equal(status, result.Status);
        Assert.Equal(before, await h.CycleStateAsync(id));
    }

    // ------------------------------------------------------------------ the whole cycle

    [Fact]
    public async Task Three_people_take_a_draft_to_published_by_four_acts()
    {
        var (h, id) = await DraftAsync();
        using var _ = h;

        var approved = await h.ApproveAsync(id, H.Approver);
        Assert.True(approved.Success, approved.Error);
        Assert.Equal(PayrollConfigPublicationStatus.Approved, approved.Status);
        var afterApproval = await h.VersionAsync(id);
        Assert.Equal(H.Approver, afterApproval.ApprovedBy);
        Assert.Equal(h.Clock.Now.UtcDateTime, afterApproval.ApprovedAt);
        Assert.Equal(PayrollConfigurationHashService.ComputeContentHash(afterApproval), afterApproval.ContentHash);
        Assert.True(afterApproval.IsImmutable);

        var attested = await h.AttestAsync(id, H.Attestor);
        Assert.True(attested.Success, attested.Error);
        await using (var db = h.Db())
        {
            var attestation = await db.LegalAttestations.AsNoTracking().SingleAsync();
            Assert.Equal((H.Attestor, "Active", afterApproval.ContentHash), (attestation.AttestedBy, attestation.Status, attestation.ContentHash));
            Assert.Equal(h.Clock.Now.UtcDateTime, attestation.AttestedAt);
            // The attestation names the decisions the version's values cite, and the exceptions it relies on.
            var decisions = JsonSerializer.Deserialize<List<string>>(attestation.DecisionIdsJson)!;
            Assert.Equal(PayrollFrameworkGovernance.CitedDecisionIds(afterApproval), decisions);
            Assert.Equal(15, decisions.Count);
            Assert.Equal([Pay054FakeLegalGuard.Law50, Pay054FakeLegalGuard.Tax], JsonSerializer.Deserialize<List<string>>(attestation.InterimExceptionIdsJson)!);
        }

        var published = await h.PublishAsync(id, H.Approver);
        Assert.True(published.Success, published.Error);
        var version = await h.VersionAsync(id);
        Assert.Equal(PayrollConfigPublicationStatus.Published, version.PublicationStatus);
        Assert.True(version.IsActive);
        Assert.Equal(H.Approver, version.PublishedBy);
        Assert.Equal(H.Approver, version.ApprovedBy);
        Assert.Equal(afterApproval.ContentHash, version.ContentHash);
        Assert.DoesNotContain(version.Editors, e => e.UserId == H.Approver || e.UserId == H.Attestor);
        await using (var db = h.Db())
        {
            var notice = await db.PayrollConfigChangeNotices.AsNoTracking().SingleAsync();
            Assert.Equal((id, version.ContentHash, H.Approver), (notice.PayrollConfigurationId, notice.ContentHash, notice.PublishedBy));
        }
    }

    [Fact]
    public async Task The_attestor_may_publish_and_a_fourth_person_may_not()
    {
        var (h, id) = await DraftAsync();
        using var _ = h;
        Assert.True((await h.ApproveAsync(id)).Success);
        Assert.True((await h.AttestAsync(id)).Success);

        await AssertRefusedAsync(h, id, () => h.PublishAsync(id, Stranger),
            PayrollFrameworkGovernance.PublisherNotApproverOrAttestor, PayrollConfigPublicationStatus.Approved);

        var published = await h.PublishAsync(id, H.Attestor);
        Assert.True(published.Success, published.Error);
        Assert.Equal(H.Attestor, (await h.VersionAsync(id)).PublishedBy);
    }

    // ------------------------------------------------------------------ approve

    [Fact]
    public async Task An_editor_does_not_approve_whether_he_imported_created_or_edited()
    {
        var (h, id) = await DraftAsync();
        using var _ = h;
        Assert.True((await h.SetEffectiveFromAsync(id, new DateOnly(2026, 2, 1), H.OtherEditor)).Success);

        await AssertRefusedAsync(h, id, () => h.ApproveAsync(id, H.Editor), PayrollFrameworkGovernance.ApproverIsEditor, PayrollConfigPublicationStatus.Draft);
        await AssertRefusedAsync(h, id, () => h.ApproveAsync(id, H.OtherEditor), PayrollFrameworkGovernance.ApproverIsEditor, PayrollConfigPublicationStatus.Draft);
        await AssertRefusedAsync(h, id, () => h.ApproveAsync(id, Guid.Empty), PayrollFrameworkGovernance.UnknownActor, PayrollConfigPublicationStatus.Draft);

        var created = await h.CreateAsync(regimeCode: "LAW_53_2021", effectiveFrom: new DateOnly(2026, 1, 1), actor: H.Approver);
        await AssertRefusedAsync(h, created.FrameworkId!.Value, () => h.ApproveAsync(created.FrameworkId.Value, H.Approver),
            PayrollFrameworkGovernance.ApproverIsEditor, PayrollConfigPublicationStatus.Draft);

        Assert.True((await h.ApproveAsync(id, H.Approver)).Success);
    }

    [Fact]
    public async Task Only_a_draft_is_approved_and_an_approval_is_not_repeated()
    {
        var (h, id) = await DraftAsync();
        using var _ = h;
        Assert.True((await h.ApproveAsync(id)).Success);

        await AssertRefusedAsync(h, id, () => h.ApproveAsync(id, Stranger), PayrollFrameworkMessages.ApproveNeedsDraft, PayrollConfigPublicationStatus.Approved);
        Assert.Equal(PayrollFrameworkFailure.NotFound, (await h.ApproveAsync(Guid.NewGuid())).Failure);
    }

    [Fact]
    public async Task A_draft_of_a_template_is_not_approved_before_its_values_are_classified_and_a_legacy_draft_never()
    {
        using var h = new H();
        var template = await h.CreateAsync(regimeCode: "LAW_50_2004", effectiveFrom: new DateOnly(2026, 1, 1));
        var id = template.FrameworkId!.Value;
        var before = await h.CycleStateAsync(id);

        var refused = await h.ApproveAsync(id);

        Assert.False(refused.Success);
        Assert.StartsWith("لا اعتماد للنسخة: ", refused.Error, StringComparison.Ordinal);
        Assert.Contains("بلا مرجع قانوني", refused.Error, StringComparison.Ordinal);
        Assert.Contains("قيمة أخرى", refused.Error, StringComparison.Ordinal);            // the first findings, and how many more
        Assert.Equal(before, await h.CycleStateAsync(id));

        Guid legacy;
        await using (var db = h.Db())
        {
            var draft = PayrollRegimeSeed.CreateLaw53Configuration();
            draft.PublicationStatus = PayrollConfigPublicationStatus.Draft;
            draft.ContentHash = null;
            db.PayrollConfigurations.Add(draft);
            await db.SaveChangesAsync();
            legacy = draft.Id;
        }

        await AssertRefusedAsync(h, legacy, () => h.ApproveAsync(legacy), PayrollFrameworkGovernance.LegacyDraft, PayrollConfigPublicationStatus.Draft);
    }

    [Fact]
    public async Task An_interim_value_that_cites_an_exception_the_catalog_does_not_hold_stops_the_approval_and_is_named()
    {
        var (h, id) = await DraftAsync(H.PackWith(p =>
        {
            p["statutoryDeductions"]![1]!["interimExceptionId"] = "EX-NOT-IN-CATALOG";
            p["taxBrackets"]![0]!["interimExceptionId"] = "EX-NOT-IN-CATALOG";
        }));
        using var _ = h;
        var before = await h.CycleStateAsync(id);

        var refused = await h.ApproveAsync(id);

        Assert.False(refused.Success);
        Assert.Equal(400, refused.StatusCode);
        Assert.Contains("الخصم النظامي DED-SOC: تستشهد بالاستثناء EX-NOT-IN-CATALOG وهو غير نافذ اليوم", refused.Error, StringComparison.Ordinal);
        Assert.Contains("شريحة الضريبة 1: تستشهد بالاستثناء EX-NOT-IN-CATALOG", refused.Error, StringComparison.Ordinal);
        Assert.Equal(before, await h.CycleStateAsync(id));
    }

    // ------------------------------------------------------------------ attest

    [Fact]
    public async Task The_attestor_is_neither_an_editor_nor_the_approver_and_attests_an_approved_version_only()
    {
        var (h, id) = await DraftAsync();
        using var _ = h;

        await AssertRefusedAsync(h, id, () => h.AttestAsync(id, H.Attestor), PayrollFrameworkMessages.AttestNeedsApproved, PayrollConfigPublicationStatus.Draft);
        Assert.True((await h.ApproveAsync(id, H.Approver)).Success);
        await AssertRefusedAsync(h, id, () => h.AttestAsync(id, H.Approver), PayrollFrameworkGovernance.AttestorIsApprover, PayrollConfigPublicationStatus.Approved);
        await AssertRefusedAsync(h, id, () => h.AttestAsync(id, H.Editor), PayrollFrameworkGovernance.AttestorIsEditor, PayrollConfigPublicationStatus.Approved);
        await AssertRefusedAsync(h, id, () => h.AttestAsync(id, Guid.Empty), PayrollFrameworkGovernance.UnknownActor, PayrollConfigPublicationStatus.Approved);

        Assert.True((await h.AttestAsync(id, H.Attestor)).Success);
        // A second attestation by another person voids the first; exactly one stays active.
        Assert.True((await h.AttestAsync(id, Stranger)).Success);
        await using var db = h.Db();
        Assert.Equal(
            [(H.Attestor, "Voided"), (Stranger, "Active")],
            (await db.LegalAttestations.AsNoTracking().ToListAsync()).OrderBy(a => a.Status == "Active").Select(a => (a.AttestedBy, a.Status)));
    }

    [Fact]
    public async Task An_attestation_checks_that_every_decision_the_version_cites_is_in_the_register()
    {
        var (h, id) = await DraftAsync();
        using var _ = h;
        Assert.True((await h.ApproveAsync(id)).Success);
        await using (var db = h.Db())
        {
            db.LegalDecisionRecords.Remove(await db.LegalDecisionRecords.SingleAsync(d => d.DecisionId == "LD-EOS-FORMULA"));
            await db.SaveChangesAsync();
        }

        var before = await h.CycleStateAsync(id);
        var refused = await h.AttestAsync(id);

        Assert.False(refused.Success);
        Assert.Contains("LD-EOS-FORMULA", refused.Error, StringComparison.Ordinal);
        Assert.Contains("سجل القرارات القانونية", refused.Error, StringComparison.Ordinal);
        Assert.Equal(before, await h.CycleStateAsync(id));
    }

    // ------------------------------------------------------------------ publish

    [Fact]
    public async Task Nothing_is_published_from_a_draft_without_an_attestation_or_by_an_editor()
    {
        var (h, id) = await DraftAsync();
        using var _ = h;

        await AssertRefusedAsync(h, id, () => h.PublishAsync(id, H.Approver), PayrollFrameworkMessages.PublishNeedsApproved, PayrollConfigPublicationStatus.Draft);
        await AssertRefusedAsync(h, id, () => h.PublishAsync(id, H.Editor), PayrollFrameworkMessages.PublishNeedsApproved, PayrollConfigPublicationStatus.Draft);
        Assert.Null((await h.VersionAsync(id)).ApprovedBy);                     // a publication attempt writes nobody in as approver

        Assert.True((await h.ApproveAsync(id)).Success);
        await AssertRefusedAsync(h, id, () => h.PublishAsync(id, H.Approver),
            "يلزم تصديق قانوني إلكتروني (LegalAttest) قبل النشر.", PayrollConfigPublicationStatus.Approved);
        await AssertRefusedAsync(h, id, () => h.PublishAsync(id, H.Editor), PayrollFrameworkGovernance.PublisherIsEditor, PayrollConfigPublicationStatus.Approved);

        Assert.True((await h.AttestAsync(id)).Success);
        await AssertRefusedAsync(h, id, () => h.PublishAsync(id, H.Editor), PayrollFrameworkGovernance.PublisherIsEditor, PayrollConfigPublicationStatus.Approved);
        await AssertRefusedAsync(h, id, () => h.PublishAsync(id, Guid.Empty), PayrollFrameworkGovernance.UnknownActor, PayrollConfigPublicationStatus.Approved);
        Assert.True((await h.PublishAsync(id, H.Approver)).Success);
        await AssertRefusedAsync(h, id, () => h.PublishAsync(id, H.Approver), PayrollFrameworkMessages.PublishNeedsApproved, PayrollConfigPublicationStatus.Published);
    }

    [Fact]
    public async Task A_second_version_supersedes_the_first_from_its_own_date_and_never_on_or_before_the_firsts_date()
    {
        using var h = new H();
        var first = await h.PublishedAsync(effectiveFrom: new DateOnly(2026, 1, 1));

        async Task<Guid> ApprovedAsync(DateOnly from)
        {
            var imported = await h.ImportAsync(effectiveFrom: from);
            Assert.True((await h.ApproveAsync(imported.FrameworkId!.Value)).Success);
            Assert.True((await h.AttestAsync(imported.FrameworkId.Value)).Success);
            return imported.FrameworkId.Value;
        }

        var sameDay = await ApprovedAsync(new DateOnly(2026, 1, 1));
        await AssertRefusedAsync(h, sameDay, () => h.PublishAsync(sameDay),
            PayrollFrameworkMessages.NotAfterPublished(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), PayrollConfigPublicationStatus.Approved);

        var later = await ApprovedAsync(new DateOnly(2027, 1, 1));
        Assert.True((await h.PublishAsync(later)).Success);
        var superseded = await h.VersionAsync(first);
        Assert.Equal(PayrollConfigPublicationStatus.Superseded, superseded.PublicationStatus);
        Assert.Equal(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), superseded.EffectiveTo);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), superseded.EffectiveFrom);
        Assert.False(superseded.IsActive);
        // December is still the first version's; January the second's.
        await using var db = h.Db();
        var repository = h.Repository(db);
        Assert.Equal(first, (await repository.GetPublishedConfigurationForPeriodAsync(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), default))!.Id);
        Assert.Equal(later, (await repository.GetPublishedConfigurationForPeriodAsync(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), default))!.Id);
    }

    /// <summary>D-PAY-13: the run's regime is its pinned version's; a posted run pinned to no version counts for every regime.</summary>
    [Theory]
    [InlineData("unpinned", PayrollRunStatus.Posted, false)]
    [InlineData("same regime", PayrollRunStatus.Posted, false)]
    [InlineData("another regime", PayrollRunStatus.Posted, true)]
    [InlineData("unpinned", PayrollRunStatus.Approved, true)]          // not posted yet
    [InlineData("unpinned", PayrollRunStatus.Calculated, true)]
    public async Task A_version_is_not_published_into_a_posted_period_of_its_regime(string pinnedTo, PayrollRunStatus status, bool allowed)
    {
        var (h, id) = await DraftAsync(effectiveFrom: new DateOnly(2026, 3, 1));
        using var _ = h;
        await using (var db = h.Db())
        {
            Guid? pin = null;
            if (pinnedTo != "unpinned")
            {
                var other = pinnedTo == "same regime" ? PayrollRegimeSeed.CreateLaw50Configuration(isActive: false) : PayrollRegimeSeed.CreateLaw53Configuration();
                other.Code = "PINNED_" + other.RegimeCode;
                other.PublicationStatus = PayrollConfigPublicationStatus.Superseded;
                db.PayrollConfigurations.Add(other);
                pin = other.Id;
            }

            db.PayrollRuns.Add(new PayrollRun
            {
                PeriodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                RunDate = new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc),
                Status = status,
                PayrollConfigurationId = pin,
            });
            await db.SaveChangesAsync();
        }

        Assert.True((await h.ApproveAsync(id)).Success);
        Assert.True((await h.AttestAsync(id)).Success);

        if (allowed)
        {
            var published = await h.PublishAsync(id);
            Assert.True(published.Success, published.Error);
        }
        else
        {
            await AssertRefusedAsync(h, id, () => h.PublishAsync(id),
                PayrollFrameworkMessages.InsidePostedPeriod(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc)),
                PayrollConfigPublicationStatus.Approved);
        }
    }

    // ------------------------------------------------------------------ activate

    [Fact]
    public async Task Activate_works_on_a_published_version_only()
    {
        var (h, id) = await DraftAsync();
        using var _ = h;

        await AssertRefusedAsync(h, id, () => h.ActivateAsync(id), PayrollFrameworkMessages.ActivateNeedsPublished, PayrollConfigPublicationStatus.Draft);
        Assert.True((await h.ApproveAsync(id)).Success);
        await AssertRefusedAsync(h, id, () => h.ActivateAsync(id), PayrollFrameworkMessages.ActivateNeedsPublished, PayrollConfigPublicationStatus.Approved);
        Assert.True((await h.AttestAsync(id)).Success);
        Assert.True((await h.PublishAsync(id)).Success);
        Assert.True((await h.ActivateAsync(id)).Success);
        Assert.True((await h.VersionAsync(id)).IsActive);
        Assert.Equal(PayrollFrameworkFailure.NotFound, (await h.ActivateAsync(Guid.NewGuid())).Failure);
    }

    // ------------------------------------------------------------------ the interim gate and its dates (P-10)

    public static TheoryData<string, string, bool> BothSidesOfTheLastDay() => new()
    {
        // the instant (UTC), what it is in Damascus, whether the exceptions that end on 2027-01-31 are still active
        { "2026-11-23T10:00:00Z", "23 November 2026", true },
        { "2027-01-30T12:00:00Z", "30 January 2027", true },
        { "2027-01-31T12:00:00Z", "31 January 2027, the last day", true },
        { "2027-01-31T20:59:00Z", "31 January 2027, 23:59 in Damascus", true },
        { "2027-01-31T21:00:00Z", "1 February 2027, 00:00 in Damascus (still 31 January in UTC)", false },
        { "2027-02-01T12:00:00Z", "1 February 2027", false },
        { "2028-06-01T00:00:00Z", "long after", false },
    };

    [Theory]
    [MemberData(nameof(BothSidesOfTheLastDay))]
    public async Task An_interim_framework_is_approved_attested_and_published_only_while_its_exceptions_are_active_that_day_in_damascus(
        string instant, string what, bool active)
    {
        var at = DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);
        Assert.NotNull(what);

        // Approve, on that day.
        var (h, id) = await DraftAsync();
        using var _ = h;
        h.Clock.Now = at;
        var approve = await h.ApproveAsync(id);
        Assert.Equal(active, approve.Success);
        if (!active)
        {
            Assert.Contains("وهو غير نافذ اليوم", approve.Error, StringComparison.Ordinal);
            Assert.Contains("EX-STR2-", approve.Error, StringComparison.Ordinal);
            Assert.Equal(PayrollConfigPublicationStatus.Draft, (await h.VersionAsync(id)).PublicationStatus);
        }

        // Attest, on that day, a version approved earlier.
        var (h2, id2) = await DraftAsync();
        using var __ = h2;
        Assert.True((await h2.ApproveAsync(id2)).Success);
        h2.Clock.Now = at;
        var attest = await h2.AttestAsync(id2);
        Assert.Equal(active, attest.Success);
        if (!active)
        {
            Assert.StartsWith("لا تصديق للنسخة: ", attest.Error, StringComparison.Ordinal);
            await using var db = h2.Db();
            Assert.Equal(0, await db.LegalAttestations.CountAsync());
        }

        // Publish, on that day, a version approved and attested earlier.
        var (h3, id3) = await DraftAsync();
        using var ___ = h3;
        Assert.True((await h3.ApproveAsync(id3)).Success);
        Assert.True((await h3.AttestAsync(id3)).Success);
        h3.Clock.Now = at;
        var publish = await h3.PublishAsync(id3);
        Assert.Equal(active, publish.Success);
        if (!active)
        {
            Assert.StartsWith("لا نشر للنسخة: ", publish.Error, StringComparison.Ordinal);
            Assert.Equal(PayrollConfigPublicationStatus.Approved, (await h3.VersionAsync(id3)).PublicationStatus);
        }
    }

    [Theory]
    [MemberData(nameof(BothSidesOfTheLastDay))]
    public async Task A_calculation_with_an_interim_framework_starts_only_while_its_exceptions_are_active_that_day_in_damascus(
        string instant, string what, bool active)
    {
        using var h = new H();
        var version = await h.VersionAsync(await h.PublishedAsync());
        h.Clock.Now = DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);
        Assert.NotNull(what);

        var refusal = PayrollInterimGate.RefusalForRun(version, h.Guard, h.Calendar);

        if (active)
        {
            Assert.Null(refusal);
        }
        else
        {
            Assert.Equal("انتهى الاستثناء القانوني EX-STR2-LAW50-UNCERTIFIED؛ انتهى الاستثناء القانوني EX-STR2-TAX-INTERIM", refusal);
        }

        Assert.Equal("إطار مؤقت — استثناء EX-STR2-LAW50-UNCERTIFIED، EX-STR2-TAX-INTERIM", PayrollInterimGate.InterimIndicator(version));
    }

    [Fact]
    public async Task One_expired_exception_is_enough_to_stop_a_calculation_and_is_the_one_named()
    {
        using var h = new H();
        var version = await h.VersionAsync(await h.PublishedAsync());
        h.Guard.LastDays[Pay054FakeLegalGuard.Tax] = new DateOnly(2026, 11, 14);           // yesterday, by the harness's clock

        Assert.Equal("انتهى الاستثناء القانوني EX-STR2-TAX-INTERIM", PayrollInterimGate.RefusalForRun(version, h.Guard, h.Calendar));

        h.Guard.LastDays.Remove(Pay054FakeLegalGuard.Law50);                                // and one the catalog no longer holds
        Assert.Contains("EX-STR2-LAW50-UNCERTIFIED", PayrollInterimGate.RefusalForRun(version, h.Guard, h.Calendar), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_gate_fails_closed_without_a_catalog_and_a_legacy_version_passes_it_always()
    {
        using var h = new H();
        var interim = await h.VersionAsync(await h.PublishedAsync());
        var legacy = PayrollRegimeSeed.CreateLaw50Configuration();
        h.Clock.Now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(PayrollInterimGate.GuardMissing, PayrollInterimGate.RefusalForRun(interim, null, h.Calendar));
        Assert.False(PayrollInterimGate.Today(null, h.Calendar)(Pay054FakeLegalGuard.Tax));       // no catalog: no exception is active
        Assert.Null(PayrollInterimGate.RefusalForRun(legacy, null, h.Calendar));
        Assert.Null(PayrollInterimGate.RefusalForRun(legacy, h.Guard, h.Calendar));
        Assert.Null(PayrollInterimGate.InterimIndicator(legacy));
        Assert.Empty(PayrollFrameworkGovernance.InterimGateFindings(legacy, _ => false));
    }

    [Fact]
    public async Task A_signed_value_needs_no_exception_and_a_version_of_signed_values_is_not_interim()
    {
        var signed = H.PackWith(p =>
        {
            void Sign(System.Text.Json.Nodes.JsonNode? node)
            {
                var value = node!.AsObject();
                value["legalStatus"] = "Signed";
                value.Remove("interimExceptionId");
            }

            foreach (var field in p["fields"]!.AsObject()) Sign(field.Value);
            foreach (var table in new[] { "taxBrackets", "statutoryDeductions", "holidays", "priorityRules" })
                foreach (var row in p[table]!.AsArray()) Sign(row);
        });
        var (h, id) = await DraftAsync(signed);
        using var _ = h;
        h.Guard.LastDays.Clear();                                              // no exception at all in the catalog
        h.Clock.Now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True((await h.ApproveAsync(id)).Success);
        Assert.True((await h.AttestAsync(id)).Success);
        Assert.True((await h.PublishAsync(id)).Success);
        var version = await h.VersionAsync(id);
        Assert.False(PayrollFrameworkGovernance.HasInterimValues(version));
        Assert.Null(PayrollInterimGate.InterimIndicator(version));
        Assert.Null(PayrollInterimGate.RefusalForRun(version, h.Guard, h.Calendar));
        await using var db = h.Db();
        Assert.Equal("[]", (await db.LegalAttestations.AsNoTracking().SingleAsync()).InterimExceptionIdsJson);
    }

    // ------------------------------------------------------------------ the real guard, and the start-up gate untouched

    private static (PayrollLegalControlGuard Guard, LegalControlCatalogProvider Catalog) RealGuard(string exceptionsJson)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pay054-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "control-catalog.json");
        System.IO.File.WriteAllText(path, "{\"card\":\"STR-002\",\"controls\":[],\"exceptions\":" + exceptionsJson + "}");
        var environment = new Mock<IHostEnvironment>();
        environment.Setup(e => e.ContentRootPath).Returns(directory);
        environment.Setup(e => e.EnvironmentName).Returns("Production");
        var catalog = new LegalControlCatalogProvider(
            Microsoft.Extensions.Options.Options.Create(new LegalControlOptions { CatalogPath = path }),
            environment.Object,
            NullLogger<LegalControlCatalogProvider>.Instance);
        return (new PayrollLegalControlGuard(Mock.Of<ILegalControlGuard>(), catalog), catalog);
    }

    [Fact]
    public void The_payroll_gate_reads_the_exceptions_last_day_and_the_shared_start_up_check_still_reads_the_status_only()
    {
        var (guard, catalog) = RealGuard("""
            [
              {"exceptionId":"EX-A","status":"Active","expiresOn":"2027-01-31"},
              {"exceptionId":"EX-ENDED","status":"Active","expiresOn":"2020-01-01"},
              {"exceptionId":"EX-REVOKED","status":"Revoked","expiresOn":"2099-01-01"},
              {"exceptionId":"EX-NO-DATE","status":"Active"},
              {"exceptionId":"EX-BAD-DATE","status":"Active","expiresOn":"31/01/2027"}
            ]
            """);
        var before = new DateOnly(2027, 1, 30);
        var lastDay = new DateOnly(2027, 1, 31);
        var after = new DateOnly(2027, 2, 1);

        Assert.True(guard.IsExceptionActive("EX-A", before));
        Assert.True(guard.IsExceptionActive("EX-A", lastDay));
        Assert.False(guard.IsExceptionActive("EX-A", after));
        Assert.True(guard.IsExceptionActive(" ex-a ", lastDay));                          // the catalog matches ids without case
        Assert.Equal(lastDay, guard.ExceptionExpiresOn("EX-A"));
        Assert.False(guard.IsExceptionActive("EX-ENDED", before));
        Assert.False(guard.IsExceptionActive("EX-REVOKED", before));
        Assert.False(guard.IsExceptionActive("EX-NO-DATE", before));                      // no readable last day: not active
        Assert.False(guard.IsExceptionActive("EX-BAD-DATE", before));
        Assert.False(guard.IsExceptionActive("EX-NOT-THERE", before));
        Assert.False(guard.IsExceptionActive(null, before));
        Assert.False(guard.IsExceptionActive("  ", before));

        // The shared check keeps its meaning (the services' start-up gate uses it): the status, not the date.
        Assert.True(catalog.IsExceptionActive("EX-A"));
        Assert.True(catalog.IsExceptionActive("EX-ENDED"));
        Assert.True(catalog.IsExceptionActive("EX-NO-DATE"));
        Assert.False(catalog.IsExceptionActive("EX-REVOKED"));
        Assert.False(catalog.IsExceptionActive("EX-NOT-THERE"));
    }

    [Fact]
    public void The_two_exceptions_the_interim_pack_cites_are_active_in_the_repositorys_catalog_until_31_january_2027()
    {
        var (guard, _) = RealGuard(JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(H.RepoRoot, "docs", "str-legal", "control-catalog.json")))
            .RootElement.GetProperty("exceptions").GetRawText());

        foreach (var id in new[] { Pay054FakeLegalGuard.Tax, Pay054FakeLegalGuard.Law50 })
        {
            Assert.Equal(new DateOnly(2027, 1, 31), guard.ExceptionExpiresOn(id));
            Assert.True(guard.IsExceptionActive(id, new DateOnly(2027, 1, 31)));
            Assert.False(guard.IsExceptionActive(id, new DateOnly(2027, 2, 1)));
        }
    }
}
