using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using H = HRManagement.UnitTests.Pay054FrameworkHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-54 (P-6): versions, dates, and which version governs a period. A version carries its own effective date; a publication never
/// cuts back in time or into a posted period, and ends its predecessor the day before and only that; a superseded version still
/// governs its own months; the active configuration is the version that governs today, the same on every call whatever the row order.
/// </summary>
public sealed class Pay054VersioningTests
{
    private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static PayrollConfiguration Published(string code, DateTime from, int version = 1, bool active = true, string? regime = null)
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration(active);
        config.Id = Guid.NewGuid();
        config.Code = code;
        config.RegimeCode = regime ?? config.RegimeCode;
        config.VersionNumber = version;
        config.EffectiveFrom = from;
        return config;
    }

    /// <summary>An approved v2 draft of <paramref name="predecessor"/>'s code, effective from <paramref name="from"/>.</summary>
    private static PayrollConfiguration ApprovedSuccessor(PayrollConfiguration predecessor, DateTime from)
    {
        var next = PayrollConfigurationVersioning.CloneAsDraft(predecessor, predecessor.VersionNumber + 1, from);
        next.Id = Guid.NewGuid();
        Assert.True(PayrollConfigurationVersioning.TryApprove(next, Guid.NewGuid()).Success);
        return next;
    }

    private static (PayrollConfigPublicationStatus, bool, DateTime, DateTime?, string?) Snapshot(PayrollConfiguration c) =>
        (c.PublicationStatus, c.IsActive, c.EffectiveFrom, c.EffectiveTo, c.ContentHash);

    // ------------------------------------------------------------------ publish

    [Fact]
    public void A_version_is_published_from_its_own_date_and_its_predecessor_ends_the_day_before_and_only_that()
    {
        var v1 = Published("LAW_50_2004", Day(2000, 1, 1));
        var v1Hash = v1.ContentHash;
        var v2 = ApprovedSuccessor(v1, Day(2031, 2, 1));
        var publisher = Guid.NewGuid();

        var (ok, error) = PayrollConfigurationVersioning.TryPublish(v2, [v1, v2], publisher);

        Assert.True(ok, error);
        Assert.Equal(PayrollConfigPublicationStatus.Published, v2.PublicationStatus);
        Assert.True(v2.IsActive);
        Assert.Equal(Day(2031, 2, 1), v2.EffectiveFrom);
        Assert.Null(v2.EffectiveTo);
        Assert.Equal(publisher, v2.PublishedBy);
        Assert.Equal(PayrollConfigPublicationStatus.Superseded, v1.PublicationStatus);
        Assert.False(v1.IsActive);
        Assert.Equal(Day(2000, 1, 1), v1.EffectiveFrom);                 // the predecessor's start does not move
        Assert.Equal(Day(2031, 1, 31), v1.EffectiveTo);                  // it ends the day before
        Assert.Equal(v1Hash, v1.ContentHash);                            // and nothing else of it changes
    }

    [Fact]
    public void A_draft_is_never_published_and_nobody_is_written_in_as_its_approver()
    {
        var v1 = Published("LAW_50_2004", Day(2000, 1, 1));
        var draft = PayrollConfigurationVersioning.CloneAsDraft(v1, 2, Day(2031, 2, 1));
        var before = (Snapshot(draft), Snapshot(v1));

        var (ok, error) = PayrollConfigurationVersioning.TryPublish(draft, [v1, draft], Guid.NewGuid());

        Assert.False(ok);
        Assert.Equal(PayrollFrameworkMessages.PublishNeedsApproved, error);
        Assert.Equal(before, (Snapshot(draft), Snapshot(v1)));
        Assert.Null(draft.ApprovedBy);
        Assert.Null(draft.ApprovedAt);
        Assert.Null(draft.PublishedBy);
    }

    [Theory]
    [InlineData(PayrollConfigPublicationStatus.Published)]
    [InlineData(PayrollConfigPublicationStatus.Superseded)]
    public void A_version_that_is_not_approved_is_not_published(PayrollConfigPublicationStatus status)
    {
        var version = Published("LAW_50_2004", Day(2000, 1, 1));
        version.PublicationStatus = status;

        Assert.False(PayrollConfigurationVersioning.TryPublish(version, [version], Guid.NewGuid()).Success);
        Assert.Equal(status, version.PublicationStatus);
    }

    [Theory]
    [InlineData(2026, 3, 1)]     // the same day as the published version
    [InlineData(2025, 12, 1)]    // before it
    [InlineData(2000, 1, 1)]
    public void A_publication_never_cuts_back_in_time(int year, int month, int day)
    {
        var v1 = Published("LAW_50_2004", Day(2026, 3, 1));
        var v2 = ApprovedSuccessor(v1, Day(year, month, day));
        var before = (Snapshot(v2), Snapshot(v1));

        var (ok, error) = PayrollConfigurationVersioning.TryPublish(v2, [v1, v2], Guid.NewGuid());

        Assert.False(ok);
        Assert.Equal(PayrollFrameworkMessages.NotAfterPublished(Day(2026, 3, 1)), error);
        Assert.Contains("2026-03-01", error);
        Assert.Equal(before, (Snapshot(v2), Snapshot(v1)));
    }

    [Theory]
    [InlineData(2026, 4, 30, false)]   // a posted April: a version from 1 March is inside a posted period
    [InlineData(2026, 3, 1, false)]    // a posted period ending on the very day
    [InlineData(2026, 2, 28, true)]    // the last posted period ended the day before
    public void A_publication_never_cuts_into_a_posted_period_of_its_regime(int year, int month, int day, bool allowed)
    {
        var v1 = Published("LAW_50_2004", Day(2000, 1, 1));
        var v2 = ApprovedSuccessor(v1, Day(2026, 3, 1));
        var before = (Snapshot(v2), Snapshot(v1));

        var (ok, error) = PayrollConfigurationVersioning.TryPublish(v2, [v1, v2], Guid.NewGuid(), latestPostedPeriodEnd: Day(year, month, day));

        Assert.Equal(allowed, ok);
        if (!allowed)
        {
            Assert.Equal(PayrollFrameworkMessages.InsidePostedPeriod(Day(2026, 3, 1), Day(year, month, day)), error);
            Assert.Equal(before, (Snapshot(v2), Snapshot(v1)));
        }
    }

    [Fact]
    public void A_version_without_brackets_is_not_published()
    {
        var v1 = Published("LAW_50_2004", Day(2000, 1, 1));
        var v2 = PayrollConfigurationVersioning.CloneAsDraft(v1, 2, Day(2031, 1, 1), c => c.TaxBrackets.Clear());
        Assert.True(PayrollConfigurationVersioning.TryApprove(v2, Guid.NewGuid()).Success);

        var (ok, error) = PayrollConfigurationVersioning.TryPublish(v2, [v1, v2], Guid.NewGuid());

        Assert.False(ok);
        Assert.Equal(PayrollFrameworkMessages.NoBrackets, error);
        Assert.Equal(PayrollConfigPublicationStatus.Approved, v2.PublicationStatus);
        Assert.Equal(PayrollConfigPublicationStatus.Published, v1.PublicationStatus);
    }

    [Fact]
    public void The_first_version_of_a_code_publishes_with_no_predecessor()
    {
        var first = PayrollFrameworkTemplate.CreateDraft("LAW_50_2004", "LAW_50_2004_NEW", null, 1, Day(2031, 1, 1));
        Assert.True(PayrollConfigurationVersioning.TryApprove(first, Guid.NewGuid()).Success);
        var other = Published("LAW_50_2004", Day(2000, 1, 1));

        Assert.True(PayrollConfigurationVersioning.TryPublish(first, [first], Guid.NewGuid()).Success);
        Assert.Equal(PayrollConfigPublicationStatus.Published, other.PublicationStatus);   // another code is not superseded
        Assert.True(other.IsActive);
    }

    // ------------------------------------------------------------------ which version governs a period

    [Fact]
    public void A_superseded_version_still_governs_its_own_months()
    {
        var v1 = Published("LAW_50_2004", Day(2000, 1, 1));
        var v2 = ApprovedSuccessor(v1, Day(2031, 2, 1));
        Assert.True(PayrollConfigurationVersioning.TryPublish(v2, [v1, v2], Guid.NewGuid()).Success);
        var all = new[] { v2, v1 };

        Assert.Equal(v1.Id, PayrollConfigurationVersioning.ResolveGoverning(all, Day(2030, 12, 1))!.Id);
        Assert.Equal(v1.Id, PayrollConfigurationVersioning.ResolveGoverning(all, Day(2031, 1, 1))!.Id);
        Assert.Equal(v1.Id, PayrollConfigurationVersioning.ResolveGoverning(all, Day(2031, 1, 31))!.Id);
        Assert.Equal(v2.Id, PayrollConfigurationVersioning.ResolveGoverning(all, Day(2031, 2, 1))!.Id);
        Assert.Equal(v2.Id, PayrollConfigurationVersioning.ResolveGoverning(all, Day(2040, 6, 1))!.Id);
        // The old resolver's entry point is the same resolver.
        Assert.Equal(v1.Id, PayrollConfigurationVersioning.ResolvePublishedForPeriod(all, Day(2031, 1, 1))!.Id);
    }

    [Fact]
    public void A_new_code_from_2031_changes_nothing_before_2031_and_governs_from_then()
    {
        var law50 = Published("LAW_50_2004", Day(2000, 1, 1));
        var ac54 = Published("LAW_50_2004_AC54", Day(2031, 1, 1));
        var all = new[] { ac54, law50 };

        Assert.Equal(law50.Id, PayrollConfigurationVersioning.ResolveGoverning(all, Day(2026, 11, 1))!.Id);
        Assert.Equal(law50.Id, PayrollConfigurationVersioning.ResolveGoverning(all, Day(2030, 12, 31))!.Id);
        Assert.Equal(ac54.Id, PayrollConfigurationVersioning.ResolveGoverning(all, Day(2031, 1, 1))!.Id);
        // Asked by regime (both codes are of the regime LAW_50_2004), the answer is the regime's governing version that day.
        Assert.Equal(ac54.Id, PayrollConfigurationVersioning.ResolveGoverning(all, Day(2031, 1, 1), "LAW_50_2004")!.Id);
        Assert.Equal(law50.Id, PayrollConfigurationVersioning.ResolveGoverning(all, Day(2030, 6, 1), "LAW_50_2004")!.Id);
        Assert.Equal(ac54.Id, PayrollConfigurationVersioning.ResolveGoverning(all, Day(2031, 1, 1), "LAW_50_2004_AC54")!.Id);
        Assert.Null(PayrollConfigurationVersioning.ResolveGoverning(all, Day(2030, 6, 1), "LAW_50_2004_AC54"));
    }

    [Fact]
    public void A_code_with_an_active_version_comes_before_a_code_without_one_and_the_answer_never_depends_on_the_row_order()
    {
        var active = Published("LAW_50_2004", Day(2000, 1, 1));
        var inactiveLater = Published("LAW_53_2021", Day(2010, 1, 1), active: false, regime: "LAW_53_2021");
        var secondActive = Published("LAW_50_2004_B", Day(2000, 1, 1));
        var rows = new[] { active, inactiveLater, secondActive };

        var expected = PayrollConfigurationVersioning.ResolveGoverning(rows, Day(2026, 11, 1))!.Id;

        // Two active codes of the same date: the code's name settles it, the same in every order.
        Assert.Equal(active.Id, expected);
        foreach (var order in new[] { new[] { 0, 1, 2 }, [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0] })
            Assert.Equal(expected, PayrollConfigurationVersioning.ResolveGoverning(order.Select(i => rows[i]).ToList(), Day(2026, 11, 1))!.Id);

        Assert.Equal(inactiveLater.Id, PayrollConfigurationVersioning.ResolveGoverning(rows, Day(2026, 11, 1), "LAW_53_2021")!.Id);
    }

    [Fact]
    public void A_draft_and_an_approved_version_govern_nothing_and_a_day_before_every_version_has_none()
    {
        var v1 = Published("LAW_50_2004", Day(2020, 1, 1));
        var approved = ApprovedSuccessor(v1, Day(2021, 1, 1));
        var draft = PayrollConfigurationVersioning.CloneAsDraft(v1, 3, Day(2022, 1, 1));

        Assert.Equal(v1.Id, PayrollConfigurationVersioning.ResolveGoverning([draft, approved, v1], Day(2026, 1, 1))!.Id);
        Assert.Null(PayrollConfigurationVersioning.ResolveGoverning([draft, approved, v1], Day(2019, 12, 31)));
        Assert.Null(PayrollConfigurationVersioning.ResolveGoverning([draft, approved], Day(2026, 1, 1)));
        Assert.Null(PayrollConfigurationVersioning.ResolveGoverning([], Day(2026, 1, 1)));
    }

    // ------------------------------------------------------------------ activate

    [Theory]
    [InlineData(PayrollConfigPublicationStatus.Draft)]
    [InlineData(PayrollConfigPublicationStatus.Approved)]
    [InlineData(PayrollConfigPublicationStatus.Superseded)]
    public void Activate_never_publishes(PayrollConfigPublicationStatus status)
    {
        var version = Published("LAW_50_2004", Day(2000, 1, 1), active: false);
        version.PublicationStatus = status;

        var (ok, error) = PayrollConfigurationVersioning.TryActivate(version, [version]);

        Assert.False(ok);
        Assert.Equal(PayrollFrameworkMessages.ActivateNeedsPublished, error);
        Assert.Equal(status, version.PublicationStatus);
        Assert.False(version.IsActive);
    }

    [Fact]
    public void Activate_makes_a_published_version_the_active_one_of_its_code()
    {
        var version = Published("LAW_53_2021", Day(2000, 1, 1), active: false, regime: "LAW_53_2021");

        Assert.True(PayrollConfigurationVersioning.TryActivate(version, [version]).Success);
        Assert.True(version.IsActive);
    }

    // ------------------------------------------------------------------ the repository: «today» is Damascus's

    [Fact]
    public async Task The_active_configuration_is_the_version_that_governs_today_the_same_on_every_call_with_two_active_codes()
    {
        using var h = new H();
        Guid law50, future;
        await using (var db = h.Db())
        {
            var a = Published("LAW_50_2004", Day(2000, 1, 1));
            var b = Published("LAW_50_2004_B", Day(2000, 1, 1));
            var next = Published("LAW_50_2004_C", Day(2027, 1, 1));
            db.PayrollConfigurations.AddRange(b, next, a);
            await db.SaveChangesAsync();
            (law50, future) = (a.Id, next.Id);
        }

        for (var i = 0; i < 5; i++)
        {
            await using var db = h.Db();
            Assert.Equal(law50, (await h.Repository(db).GetActiveConfigurationAsync(CancellationToken.None))!.Id);
        }

        await using (var db = h.Db())
        {
            var repository = h.Repository(db);
            var active = await repository.GetActiveConfigurationAsync(CancellationToken.None);
            Assert.Equal(5, active!.TaxBrackets.Count);
            Assert.Equal(law50, (await repository.GetConfigurationByRegimeCodeAsync("LAW_50_2004", CancellationToken.None))!.Id);
            Assert.Equal(future, (await repository.GetPublishedConfigurationForPeriodAsync(Day(2027, 1, 1), CancellationToken.None))!.Id);
            Assert.Null(await repository.GetConfigurationByRegimeCodeAsync("CIVIL_SERVICE_2026", CancellationToken.None));
        }

        // 31 December, 22:30 UTC is already 1 January in Damascus: the version of 1 January governs «today».
        h.Clock.Now = new DateTimeOffset(2026, 12, 31, 22, 30, 0, TimeSpan.Zero);
        await using (var db = h.Db())
            Assert.Equal(future, (await h.Repository(db).GetActiveConfigurationAsync(CancellationToken.None))!.Id);

        h.Clock.Now = new DateTimeOffset(2026, 12, 31, 20, 30, 0, TimeSpan.Zero);
        await using (var db = h.Db())
            Assert.Equal(law50, (await h.Repository(db).GetActiveConfigurationAsync(CancellationToken.None))!.Id);
    }
}
