using HR.Core.Domain.Entities.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-47 §3 on PostgreSQL: two publications of one template race on the template's xmin; one wins and the other gets the 409
/// «نُشر إصدار آخر للتو؛ أعد المحاولة», with nothing of it saved.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pol047ConcurrentPublishPostgresTests(PostgreSqlFixture fixture)
{
    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_concurrent_publications_of_one_template_one_wins_the_other_gets_409()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "race");
        (Guid Template, Guid Published, Guid Draft, Guid PublishedRule, Guid DraftRule) seeded;
        await using (var db = Pol047Pg.AdminContext(admin))
            seeded = await Pol047Pg.SeedTemplateAsync(db, Pol047Pg.National, PolicyTemplateScope.National, PolicyDomains.AssetsCustody);

        await using var first = Pol047Pg.AdminContext(admin);
        await using var second = Pol047Pg.AdminContext(admin);
        var cache = new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions()));

        // Both approvers have the draft and its template open (as two requests would) before either saves.
        await second.PolicyTemplateVersions.Include(v => v.PolicyTemplate).Include(v => v.Rules).SingleAsync(v => v.Id == seeded.Draft);

        var won = await Pol047Pg.PublishService(first, new PolicyVersionPublisher(first, cache))
            .PublishAsync(new(seeded.Draft, Pol047Pg.Approver(Guid.NewGuid()), "EV-1"));
        var lost = await Pol047Pg.PublishService(second, new PolicyVersionPublisher(second, cache))
            .PublishAsync(new(seeded.Draft, Pol047Pg.Approver(Guid.NewGuid()), "EV-2"));

        Assert.True(won.Succeeded, won.MessageAr);
        Assert.Equal(PolicyPublishOutcome.Conflict, lost.Outcome);
        Assert.Equal(PolicyPublishService.ConcurrentPublishMessage, lost.MessageAr);
        Assert.Equal(1L, await Pol047Pg.ScalarAsync<long>(admin, $"""SELECT count(*) FROM "PolicyPublications" WHERE "PolicyTemplateVersionId" = '{seeded.Draft}'"""));
        Assert.Equal("EV-1", await Pol047Pg.ScalarAsync<string>(admin, $"""SELECT "EvidenceRef" FROM "PolicyPublications" WHERE "PolicyTemplateVersionId" = '{seeded.Draft}'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_draft_changed_after_the_review_is_not_published()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "review");
        (Guid Template, Guid Published, Guid Draft, Guid PublishedRule, Guid DraftRule) seeded;
        await using (var db = Pol047Pg.AdminContext(admin))
            seeded = await Pol047Pg.SeedTemplateAsync(db, Pol047Pg.National, PolicyTemplateScope.National, PolicyDomains.AssetsCustody);
        var cache = new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions()));

        // The approver reviews the draft (its revision), then the author adds a rule.
        uint reviewed;
        await using (var review = Pol047Pg.AdminContext(admin))
        {
            var service = Pol047Pg.PublishService(review, new PolicyVersionPublisher(review, cache));
            reviewed = service.Revision(await review.PolicyTemplateVersions.SingleAsync(v => v.Id == seeded.Draft))!.Value;
        }

        await using (var author = Pol047Pg.AdminContext(admin))
        {
            var drafts = new PolicyDraftService(author, new PolicyRuleValidationService(author, HR.Core.Domain.Services.Policy.AllowAllPolicyDomainCapabilities.Instance),
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            var added = await drafts.AddRuleAsync(seeded.Draft, new("R-LATE", "late", 20, Pol047Pg.ValidWhen, Pol047Pg.ValidThen), Guid.NewGuid());
            Assert.True(added.Succeeded, added.MessageAr);
        }

        await using var publish = Pol047Pg.AdminContext(admin);
        var result = await Pol047Pg.PublishService(publish, new PolicyVersionPublisher(publish, cache))
            .PublishAsync(new(seeded.Draft, Pol047Pg.Approver(Guid.NewGuid()), "EV-REVIEW", reviewed));

        Assert.Equal(PolicyPublishOutcome.Conflict, result.Outcome);
        Assert.Equal(PolicyPublishService.ChangedSinceReviewMessage, result.MessageAr);
        Assert.Equal("Draft", await Pol047Pg.ScalarAsync<string>(admin, $"""SELECT "Status" FROM "PolicyTemplateVersions" WHERE "Id" = '{seeded.Draft}'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_concurrent_drafts_of_one_template_meet_the_one_draft_index()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var admin = await Pol047Pg.MigratedAsync(fixture, "drafts");
        (Guid Template, Guid Published, Guid Draft, Guid PublishedRule, Guid DraftRule) seeded;
        await using (var db = Pol047Pg.AdminContext(admin))
            seeded = await Pol047Pg.SeedTemplateAsync(db, Pol047Pg.National, PolicyTemplateScope.National, PolicyDomains.AssetsCustody);

        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => Pol047Pg.ExecAsync(admin, $"""
            INSERT INTO "PolicyTemplateVersions" ("Id","PolicyTemplateId","VersionNumber","Status","TenantId","CreatedAt","UpdatedAt","WithdrawsAllRules")
            VALUES (gen_random_uuid(),'{seeded.Template}',3,'Draft','{Pol047Pg.National}',now(),now(),false);
            """));
        Assert.Equal("23505", ex.SqlState);
        Assert.Equal(HRCoreDbContext.OneDraftPerTemplateIndex, ex.ConstraintName);
    }
}
