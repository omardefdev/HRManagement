using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Controllers;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-030 (G4): the candidate retention screen <c>CandidateLifecycle/Index</c> and its JSON list. The requisitions are
/// closed years before the real clock (the controller uses <see cref="DateTime.UtcNow"/>), so no fixed date matters.
/// </summary>
public class Hcm030CandidateLifecycleScreenTests
{
    private static readonly DateTime LongAgo = DateTime.UtcNow.AddYears(-6);
    private static readonly Guid AdminId = Guid.Parse("30300000-0000-4000-8000-0000000000ae");

    [Fact]
    public void Controller_is_admin_only()
    {
        var authorize = Assert.Single(typeof(CandidateLifecycleController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());
        Assert.Equal(HrManagementPolicies.HrCoreDelete, authorize.Policy);
        foreach (var action in new[] { nameof(CandidateLifecycleController.DryRun), nameof(CandidateLifecycleController.Apply) })
        {
            var method = typeof(CandidateLifecycleController).GetMethod(action)!;
            Assert.NotNull(method.GetCustomAttributes(typeof(HttpPostAttribute), false).SingleOrDefault());
            Assert.NotNull(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false).SingleOrDefault());
        }
    }

    [Fact]
    public async Task Index_lists_due_pairs_with_the_reference_only_and_the_run_history()
    {
        await using var db = Hcm030RetentionJobTests.CreateContext();
        var pair = await Hcm030RetentionJobTests.SeedPublicPairAsync(db, "S1", closedAt: LongAgo);
        await Hcm030RetentionJobTests.SeedPublicPairAsync(db, "S2", closedAt: null);
        var controller = CreateController(db, apply: false);

        var view = Assert.IsType<ViewResult>(await controller.Index(new SearchFilterBase(), default));
        var model = Assert.IsType<CandidateLifecycleIndexViewModel>(view.Model);

        var item = Assert.Single(model.Due.Items);
        Assert.Equal(pair.Citizen.ReferenceNumber, item.Reference);
        Assert.Equal(pair.Requisition.RequisitionNumber, item.RequisitionNumber);
        Assert.Equal(1, model.Counts.Due);
        Assert.False(model.ApplyEnabled);
        Assert.Equal(36, model.RetentionMonths);
        Assert.Empty(model.Runs);
        Assert.Contains("36", CandidateLifecycleIndexViewModel.ReasonLabel(item, model.RetentionMonths));
        Assert.Contains("إغلاق الشاغر", CandidateLifecycleIndexViewModel.ReasonLabel(item, model.RetentionMonths));
        // The due item type carries no applicant field beyond the reference.
        Assert.DoesNotContain(typeof(CandidateRetentionDueItem).GetProperties(),
            p => p.Name.Contains("Name", StringComparison.Ordinal) || p.Name.Contains("National", StringComparison.Ordinal)
                 || p.Name.Contains("Email", StringComparison.Ordinal) || p.Name.Contains("Phone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dry_run_logs_a_run_for_the_admins_tenant_and_changes_nothing()
    {
        await using var db = Hcm030RetentionJobTests.CreateContext();
        var pair = await Hcm030RetentionJobTests.SeedPublicPairAsync(db, "S3", closedAt: LongAgo);
        var controller = CreateController(db, apply: true);

        var result = Assert.IsType<RedirectToActionResult>(await controller.DryRun(default));

        Assert.Equal(nameof(CandidateLifecycleController.Index), result.ActionName);
        Assert.Contains("التشغيل التجريبي", controller.TempData["Success"]?.ToString());
        var run = await db.CandidateRetentionRuns.AsNoTracking().SingleAsync();
        Assert.True(run.DryRun);
        Assert.Equal(1, run.DueCount);
        Assert.Equal(Hcm030TestSupport.TenantA, run.TenantId);
        Assert.Equal(AdminId.ToString(), run.TriggeredBy);
        Assert.Null((await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == pair.App.Id)).AnonymizedAt);

        var view = Assert.IsType<ViewResult>(await controller.Index(new SearchFilterBase(), default));
        Assert.Single(Assert.IsType<CandidateLifecycleIndexViewModel>(view.Model).Runs);
    }

    [Fact]
    public async Task Manual_apply_is_not_bound_to_the_request_abort_token()
    {
        // A started apply commits pair by pair: a closed tab or a gateway timeout must not stop it half-way unlogged.
        await using var db = Hcm030RetentionJobTests.CreateContext();
        var pair = await Hcm030RetentionJobTests.SeedPublicPairAsync(db, "S6", closedAt: LongAgo);
        var controller = CreateController(db, apply: true);

        Assert.IsType<RedirectToActionResult>(await controller.Apply(new CancellationToken(canceled: true)));

        var run = await db.CandidateRetentionRuns.AsNoTracking().SingleAsync();
        Assert.Equal(1, run.AnonymizedCount);
        Assert.NotNull(run.FinishedAt);
        Assert.Null(run.Error);
        Assert.NotNull((await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == pair.App.Id)).AnonymizedAt);
    }

    [Fact]
    public async Task Apply_is_refused_while_retention_apply_is_false()
    {
        await using var db = Hcm030RetentionJobTests.CreateContext();
        var pair = await Hcm030RetentionJobTests.SeedPublicPairAsync(db, "S4", closedAt: LongAgo);
        var controller = CreateController(db, apply: false);

        Assert.IsType<RedirectToActionResult>(await controller.Apply(default));

        Assert.NotNull(controller.TempData["Error"]);
        Assert.Empty(await db.CandidateRetentionRuns.AsNoTracking().ToListAsync());
        Assert.Null((await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == pair.App.Id)).AnonymizedAt);
    }

    [Fact]
    public async Task Apply_anonymizes_the_due_pairs_when_enabled()
    {
        await using var db = Hcm030RetentionJobTests.CreateContext();
        var pair = await Hcm030RetentionJobTests.SeedPublicPairAsync(db, "S5", closedAt: LongAgo);
        var controller = CreateController(db, apply: true);

        Assert.IsType<RedirectToActionResult>(await controller.Apply(default));

        Assert.Contains("1", controller.TempData["Success"]?.ToString());
        var run = await db.CandidateRetentionRuns.AsNoTracking().SingleAsync();
        Assert.False(run.DryRun);
        Assert.Equal(1, run.AnonymizedCount);
        Assert.NotNull((await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == pair.App.Id)).AnonymizedAt);
        Assert.Empty(await db.CandidateDocuments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RetentionCandidates_pages_and_uses_the_pair_rule()
    {
        await using var db = Hcm030RetentionJobTests.CreateContext();
        await Hcm030RetentionJobTests.SeedPublicPairAsync(db, "J1", closedAt: LongAgo);
        await Hcm030RetentionJobTests.SeedPublicPairAsync(db, "J2", closedAt: LongAgo.AddDays(1));
        await Hcm030RetentionJobTests.SeedPublicPairAsync(db, "J3", closedAt: null);
        var controller = CreateController(db, apply: false);

        var ok = Assert.IsType<OkObjectResult>(await controller.RetentionCandidates(page: 2, pageSize: 1));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(2, json.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("page").GetInt32());
        var candidate = Assert.Single(json.RootElement.GetProperty("candidates").EnumerateArray());
        Assert.Equal(nameof(RecruitmentApplication), candidate.GetProperty("type").GetString());
        Assert.StartsWith("CA-RET-J", candidate.GetProperty("ReferenceNumber").GetString());
    }

    private static CandidateLifecycleController CreateController(HRCoreDbContext db, bool apply)
    {
        var options = Options.Create(new RecruitmentOptions { Retention = new RecruitmentRetentionOptions { Apply = apply } });
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IOptions<RecruitmentOptions>>(options);
        services.AddSingleton<ILogger<CandidateRetentionService>>(NullLogger<CandidateRetentionService>.Instance);
        services.AddScoped<IFileReferenceOutbox>(sp => new EfFileReferenceOutbox(sp.GetRequiredService<HRCoreDbContext>()));
        services.AddScoped<CandidateRetentionService>();
        var provider = services.BuildServiceProvider();

        var controller = new CandidateLifecycleController(
            db,
            new CandidateRetentionService(db, new EfFileReferenceOutbox(db), options, NullLogger<CandidateRetentionService>.Instance),
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            new AdminTenant());
        var admin = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, AdminId.ToString()), new Claim(ClaimTypes.Role, HrManagementRoles.Admin)], "test"));
        return Hcm030TestSupport.WithContext(controller, admin);
    }

    private sealed class AdminTenant : ITenantContext
    {
        public Guid? TenantId => Hcm030TestSupport.TenantA;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => Hcm030TestSupport.TenantA;
    }
}
