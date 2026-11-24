using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollConfigurations;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-54, what the independent review found and what closes it: the authors of an unpublished version stay the editors of its
/// copy; an attestation and a publication recompute the fingerprint the approval fixed; a version's priority rules are never the
/// general table's active rows; and the dashboard warns for a superseded version that still governs today.
/// </summary>
public sealed class Pay054ReviewFixesTests
{
    private static readonly DateOnly January = new(2031, 1, 1);

    [Fact]
    public async Task The_author_of_a_draft_does_not_approve_a_colleagues_copy_of_it_and_a_copy_of_a_published_version_starts_clean()
    {
        using var h = new Pay054FrameworkHarness();
        var draft = (await h.ImportAsync(code: "LAW_50_2004_COPY", effectiveFrom: January, actor: Pay054FrameworkHarness.Editor)).FrameworkId!.Value;

        // A colleague copies the draft: its content is still the first author's.
        var copy = await h.CreateAsync(cloneFromId: draft, effectiveFrom: January.AddMonths(1), actor: Pay054FrameworkHarness.OtherEditor);
        Assert.True(copy.Success, copy.Error);
        var editors = (await h.VersionAsync(copy.FrameworkId!.Value)).Editors.Select(e => e.UserId).Distinct().OrderBy(id => id).ToList();
        Assert.Equal(new[] { Pay054FrameworkHarness.Editor, Pay054FrameworkHarness.OtherEditor }.OrderBy(id => id), editors);
        var byAuthor = await h.ApproveAsync(copy.FrameworkId.Value, Pay054FrameworkHarness.Editor);
        Assert.False(byAuthor.Success);
        Assert.Equal(PayrollFrameworkGovernance.ApproverIsEditor, byAuthor.Error);
        Assert.Equal(PayrollConfigPublicationStatus.Draft, (await h.VersionAsync(copy.FrameworkId.Value)).PublicationStatus);

        // A published version's content was approved and attested by others: its copy has one editor, the person who made it.
        var published = await h.PublishedAsync("LAW_50_2004_PUB", January);
        var next = await h.CreateAsync(cloneFromId: published, effectiveFrom: January.AddMonths(2), actor: Pay054FrameworkHarness.OtherEditor);
        Assert.True(next.Success, next.Error);
        Assert.Equal([Pay054FrameworkHarness.OtherEditor], (await h.VersionAsync(next.FrameworkId!.Value)).Editors.Select(e => e.UserId).Distinct().ToList());
        Assert.True((await h.ApproveAsync(next.FrameworkId.Value, Pay054FrameworkHarness.Editor)).Success);
    }

    [Fact]
    public async Task Content_that_changed_under_an_approved_fingerprint_is_neither_attested_nor_published()
    {
        using var h = new Pay054FrameworkHarness();
        var id = (await h.ImportAsync(code: "LAW_50_2004_FIX", effectiveFrom: January)).FrameworkId!.Value;
        Assert.True((await h.ApproveAsync(id)).Success);
        var approved = await h.VersionAsync(id);

        // A write that bypassed the handlers (the race the row version closes on PostgreSQL): a rate moves under the fingerprint.
        await using (var db = h.Db())
        {
            var bracket = await db.TaxBrackets.Where(b => b.PayrollConfigurationId == id).OrderBy(b => b.SortOrder).FirstAsync();
            bracket.RatePercent += 1m;
            await db.SaveChangesAsync();
        }

        var before = await h.CycleStateAsync(id);
        var attested = await h.AttestAsync(id);
        Assert.False(attested.Success);
        Assert.Equal(PayrollFrameworkGovernance.ContentChangedAfterApproval, attested.Error);
        Assert.Equal(before, await h.CycleStateAsync(id));

        // The same content attested before the change, then changed: the publication recomputes too.
        await using (var db = h.Db())
        {
            var bracket = await db.TaxBrackets.Where(b => b.PayrollConfigurationId == id).OrderBy(b => b.SortOrder).FirstAsync();
            bracket.RatePercent -= 1m;
            await db.SaveChangesAsync();
        }

        Assert.True(PayrollFrameworkGovernance.FingerprintHolds(await h.VersionAsync(id)));
        Assert.True((await h.AttestAsync(id)).Success);
        await using (var db = h.Db())
        {
            var rule = await db.StatutoryDeductionRules.Where(r => r.PayrollConfigurationId == id && r.Code == "DED-SOC").SingleAsync();
            rule.EmployeePercent = 1m;
            await db.SaveChangesAsync();
        }

        before = await h.CycleStateAsync(id);
        var published = await h.PublishAsync(id);
        Assert.False(published.Success);
        Assert.Equal(PayrollFrameworkGovernance.ContentChangedAfterApproval, published.Error);
        Assert.Equal(before, await h.CycleStateAsync(id));
        Assert.Equal(approved.ContentHash, (await h.VersionAsync(id)).ContentHash);
    }

    [Fact]
    public async Task A_versions_priority_rules_are_never_active_rows_of_the_general_table()
    {
        using var h = new Pay054FrameworkHarness();
        var published = await h.PublishedAsync("LAW_50_2004_RULES", January);
        var copy = await h.CreateAsync(cloneFromId: published, effectiveFrom: January.AddMonths(1), actor: Pay054FrameworkHarness.OtherEditor);
        Assert.True(copy.Success, copy.Error);

        await using var db = h.Db();
        var rules = await db.DisbursementPriorityRules.AsNoTracking().ToListAsync();
        Assert.Equal(6, rules.Count);                                                  // three of the version, three of its copy
        Assert.All(rules, rule =>
        {
            Assert.NotNull(rule.PayrollConfigurationId);
            Assert.Equal(rule.PayrollConfigurationId, rule.RuleSetId);                 // the version is the rule set of its rules
            // What the release before PLAN-54 reads for a decision that names no rule set: every active row. None of these.
            Assert.False(rule.IsActive);
        });
    }

    [Fact]
    public async Task A_pack_that_repeats_the_register_writes_no_decision_row_and_one_changed_decision_writes_one()
    {
        using var h = new Pay054FrameworkHarness();
        Assert.True((await h.ImportAsync(code: "LAW_50_2004_REG", effectiveFrom: January)).Success);
        Dictionary<string, DateTime> before;
        await using (var db = h.Db())
            before = await db.LegalDecisionRecords.AsNoTracking().ToDictionaryAsync(d => d.DecisionId, d => d.UpdatedAt);
        Assert.Equal(17, before.Count);

        // The same pack an hour later, under another code: the register is as it was, row for row.
        h.Clock.Now = h.Clock.Now.AddHours(1);
        Assert.True((await h.ImportAsync(code: "LAW_50_2004_REG2", effectiveFrom: January)).Success);
        await using (var db = h.Db())
        {
            var after = await db.LegalDecisionRecords.AsNoTracking().ToDictionaryAsync(d => d.DecisionId, d => d.UpdatedAt);
            Assert.Equal(before, after);
        }

        // One decision says something else: that row is written, the sixteen others are not.
        h.Clock.Now = h.Clock.Now.AddHours(1);
        var changed = Pay054FrameworkHarness.PackWith(pack =>
            pack["decisions"]!.AsArray().Single(d => d!["decisionId"]!.GetValue<string>() == "LD-TAX-BRACKETS")!["officialSource"] = "المرسوم التشريعي 24 لعام 2003");
        Assert.True((await h.ImportAsync(changed, code: "LAW_50_2004_REG3", effectiveFrom: January)).Success);
        await using (var db = h.Db())
        {
            var after = await db.LegalDecisionRecords.AsNoTracking().ToDictionaryAsync(d => d.DecisionId, d => d.UpdatedAt);
            Assert.Equal(["LD-TAX-BRACKETS"], after.Where(row => row.Value != before[row.Key]).Select(row => row.Key).ToList());
        }
    }

    [Fact]
    public async Task The_expiry_warning_names_a_superseded_version_while_it_still_governs_today()
    {
        // 10 December 2026: the exceptions end in 52 days. Version 2 is published from 15 January 2027, so version 1 is superseded
        // and still governs until 14 January.
        using var h = new Pay054FrameworkHarness(new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        var first = await h.PublishedAsync("LAW_50_2004_WARN", new DateOnly(2026, 1, 1));
        var second = await h.ImportAsync(code: "LAW_50_2004_WARN", effectiveFrom: new DateOnly(2027, 1, 15));
        Assert.True(second.Success, second.Error);
        Assert.True((await h.ApproveAsync(second.FrameworkId!.Value)).Success);
        Assert.True((await h.AttestAsync(second.FrameworkId.Value)).Success);
        Assert.True((await h.PublishAsync(second.FrameworkId.Value)).Success);
        Assert.Equal(PayrollConfigPublicationStatus.Superseded, (await h.VersionAsync(first)).PublicationStatus);

        await using (var db = h.Db())
        {
            var warnings = await PayrollFrameworkIndicators.ExpiringAsync(h.Repository(db), h.Guard, h.Calendar, CancellationToken.None);
            Assert.Equal(2, warnings.Count);
            Assert.All(warnings, w =>
            {
                Assert.Equal(52, w.DaysLeft);
                Assert.Contains("LAW_50_2004_WARN v1", w.Frameworks);
                Assert.Contains("LAW_50_2004_WARN v2", w.Frameworks);
            });
        }

        // From 15 January the first version governs nothing: only the second is named.
        h.Clock.Now = new DateTimeOffset(2027, 1, 15, 12, 0, 0, TimeSpan.Zero);
        await using (var db = h.Db())
        {
            var warnings = await PayrollFrameworkIndicators.ExpiringAsync(h.Repository(db), h.Guard, h.Calendar, CancellationToken.None);
            Assert.All(warnings, w =>
            {
                Assert.DoesNotContain("LAW_50_2004_WARN v1", w.Frameworks);
                Assert.Contains("LAW_50_2004_WARN v2", w.Frameworks);
            });
        }
    }
}
