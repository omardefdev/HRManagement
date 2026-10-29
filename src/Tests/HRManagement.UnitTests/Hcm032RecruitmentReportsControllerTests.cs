using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Controllers;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using static HRManagement.UnitTests.Hcm032MetricsFixture;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-032 (PLAN-32): «تقارير التوظيف» — scope through the real <see cref="OrgScopeService"/> (AC-HCM-32b), the names rule
/// (decision 7), culture-safe dates, and the CSP/accessibility rules of the views.
/// </summary>
public class Hcm032RecruitmentReportsControllerTests
{
    private static readonly RecruitmentReportFilterForm PeriodForm = new() { From = "2026-01-01", To = "2026-11-01" };

    internal static ClaimsPrincipal Hr() => Principal(HrManagementRoles.HrCore);

    internal static ClaimsPrincipal Executive() => Principal(HrManagementRoles.HrCoreExecutive);

    internal static ClaimsPrincipal BranchManager(string? scopeClaim = null) =>
        Principal(HrManagementRoles.BranchManager, scopeClaim ?? UnitBranch.ToString());

    private static ClaimsPrincipal Principal(string role, string? scopeClaim = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role), new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (scopeClaim is not null)
            claims.Add(new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, scopeClaim));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    internal static IAuthorizationService Authorization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    internal static RecruitmentReportsController Controller(HRCoreDbContext db, ClaimsPrincipal user)
    {
        var controller = new RecruitmentReportsController(
            CreateService(db),
            new OrgScopeService(db, new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions()))),
            Authorization(),
            db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
        return controller;
    }

    internal static async Task<HRCoreDbContext> SeededAsync()
    {
        var db = Hcm027TestSupport.CreateContext();
        await Hcm032MetricsFixture.SeedAsync(db);
        db.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public void Controller_requires_hr_core_read_only()
    {
        var attribute = typeof(RecruitmentReportsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(HrManagementPolicies.HrCoreReadOnly, attribute?.Policy);
    }

    [Fact]
    public async Task Unrestricted_hr_sees_every_unit_and_the_competitions()
    {
        await using var db = await SeededAsync();

        var view = Assert.IsType<ViewResult>(await Controller(db, Hr()).Index(PeriodForm, CancellationToken.None));
        var vm = Assert.IsType<RecruitmentReportsViewModel>(view.Model);

        Assert.Equal(27, vm.Funnel.CohortCount);
        Assert.False(vm.IsScoped);
        Assert.True(vm.Competitions.Visible);
        Assert.Equal(2, vm.Approvals.OverdueCount);
        Assert.Equal(2, vm.OrgUnits.Count);
        Assert.NotEmpty(vm.CompetitionOptions);
    }

    [Fact]
    public async Task Branch_manager_sees_only_the_branch()
    {
        await using var db = await SeededAsync();

        var view = Assert.IsType<ViewResult>(await Controller(db, BranchManager()).Index(PeriodForm, CancellationToken.None));
        var vm = Assert.IsType<RecruitmentReportsViewModel>(view.Model);

        Assert.True(vm.IsScoped);
        Assert.Equal(12, vm.Funnel.CohortCount);
        Assert.False(vm.Competitions.Visible);
        Assert.Empty(vm.CompetitionOptions);
        Assert.Equal([UnitBranch.ToString()], vm.OrgUnits.Select(u => u.Value));
        Assert.Equal(0, vm.Approvals.OverdueCount);
    }

    [Fact]
    public async Task Branch_manager_asking_for_another_unit_gets_no_data()
    {
        await using var db = await SeededAsync();
        var form = new RecruitmentReportFilterForm { From = "2026-01-01", To = "2026-11-01", OrgUnitId = UnitHq };

        var view = Assert.IsType<ViewResult>(await Controller(db, BranchManager()).Index(form, CancellationToken.None));
        var vm = Assert.IsType<RecruitmentReportsViewModel>(view.Model);

        Assert.Equal(0, vm.Funnel.CohortCount);
        Assert.Equal(0, vm.Requisitions.Total);
        Assert.Equal(0, vm.Offers.Decided);
        Assert.Equal(0, vm.Vacancies.StalePublishedCount);
        Assert.Equal(0, vm.Approvals.InstancesExamined);
    }

    [Fact]
    public async Task Invalid_scope_claim_is_forbidden()
    {
        await using var db = await SeededAsync();

        Assert.IsType<ForbidResult>(await Controller(db, BranchManager("not-a-guid")).Index(PeriodForm, CancellationToken.None));
        Assert.IsType<ForbidResult>(await Controller(db, BranchManager("not-a-guid")).Requisition(R3, CancellationToken.None));
    }

    [Fact]
    public async Task Requisition_page_shows_names_only_to_hr_core_write()
    {
        await using var db = await SeededAsync();

        var hr = Assert.IsType<RecruitmentRequisitionReportViewModel>(
            Assert.IsType<ViewResult>(await Controller(db, Hr()).Requisition(R3, CancellationToken.None)).Model);
        var executive = Assert.IsType<RecruitmentRequisitionReportViewModel>(
            Assert.IsType<ViewResult>(await Controller(db, Executive()).Requisition(R3, CancellationToken.None)).Model);

        Assert.True(hr.ShowNames);
        Assert.All(hr.Detail.Offers, o => Assert.StartsWith(CandidateNamePrefix, o.CandidateName));
        Assert.False(executive.ShowNames);
        Assert.All(executive.Detail.Offers, o => Assert.Null(o.CandidateName));
        Assert.All(executive.Detail.Offers, o => Assert.Matches("^(PUB|INT|HR|LEG)-[0-9A-F]{8}$", o.ApplicationReference));
    }

    [Fact]
    public async Task Requisition_page_outside_the_scope_is_not_found()
    {
        await using var db = await SeededAsync();

        Assert.IsType<NotFoundResult>(await Controller(db, BranchManager()).Requisition(R1, CancellationToken.None));
        Assert.IsType<ViewResult>(await Controller(db, BranchManager()).Requisition(R3, CancellationToken.None));
    }

    [Fact]
    public async Task Dates_parse_and_print_gregorian_under_an_arabic_hijri_culture()
    {
        await using var db = await SeededAsync();
        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("ar-SA");
            var view = Assert.IsType<ViewResult>(await Controller(db, Hr()).Index(PeriodForm, CancellationToken.None));
            var vm = Assert.IsType<RecruitmentReportsViewModel>(view.Model);

            Assert.Equal((new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 1)), (vm.Filter.From, vm.Filter.To));
            Assert.Equal(27, vm.Funnel.CohortCount);
            Assert.Equal("2026-01-01 — 2026-10-31", RecruitmentReportFormat.PeriodLabel(vm.Filter));
            Assert.Equal("2026-02-03", RecruitmentReportFormat.Date(vm.Vacancies.StalePublished[0].PublishedAt));
            Assert.Equal("2026-10-29 12:00 UTC", RecruitmentReportFormat.DateTimeUtc(vm.GeneratedAt));
            Assert.Equal("66.7%", RecruitmentReportFormat.Percent(vm.Funnel.CoveragePercent));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (culture, uiCulture);
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("1447-05-10", "x")]
    [InlineData("2026-11-01", "2026-01-01")]
    public void Missing_invalid_or_reversed_dates_fall_back_to_the_default_period(string? from, string? to)
    {
        var defaults = HR.Core.Infrastructure.Services.Recruitment.Analytics.RecruitmentMetricsFilter.DefaultPeriod(Now, 12);
        var filter = new RecruitmentReportFilterForm { From = from, To = to }.ToFilter(defaults);
        if (from == "1447-05-10")
            Assert.Equal(new DateOnly(1447, 5, 10), filter.From); // a valid Gregorian string is taken as typed
        else
            Assert.Equal((defaults.From, defaults.To), (filter.From, filter.To));
    }

    [Fact]
    public void Views_follow_the_csp_and_accessibility_rules()
    {
        foreach (var name in new[] { "Index", "Requisition" })
        {
            var view = System.IO.File.ReadAllText(RepoPaths.Resolve("src", "Services", "HRCore", "HR.Core.Web", "Views", "RecruitmentReports", name + ".cshtml"));

            Assert.DoesNotMatch(new Regex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase), view);
            Assert.DoesNotMatch(new Regex(@"\sstyle\s*=", RegexOptions.IgnoreCase), view);
            Assert.DoesNotContain("confirm(", view);
            Assert.DoesNotContain("NationalId", view);
            // Inline scripts only as JSON data blocks; code comes from wwwroot/js.
            foreach (Match script in Regex.Matches(view, @"<script(?<attrs>[^>]*)>"))
            {
                var attrs = script.Groups["attrs"].Value;
                Assert.True(attrs.Contains("src=") || attrs.Contains("type=\"application/json\""), $"{name}: inline script {attrs}");
            }
            var tables = Regex.Matches(view, "<table").Count;
            Assert.Equal(tables, Regex.Matches(view, "<caption>").Count);
            foreach (Match canvas in Regex.Matches(view, "<canvas[^>]*>"))
            {
                Assert.Contains("role=\"img\"", canvas.Value);
                Assert.Contains("aria-label=", canvas.Value);
            }
        }
    }

    [Fact]
    public void Chart_script_reads_the_json_block()
    {
        var js = System.IO.File.ReadAllText(RepoPaths.Resolve("src", "Services", "HRCore", "HR.Core.Web", "wwwroot", "js", "recruitment-reports.js"));
        Assert.Contains("recruitment-report-data", js);
        Assert.DoesNotContain("innerHTML", js);
    }

    [Fact]
    public void Recruitment_menu_links_the_reports()
    {
        var layout = System.IO.File.ReadAllText(RepoPaths.Resolve("src", "Services", "HRCore", "HR.Core.Web", "Views", "Shared", "_Layout.cshtml"));
        Assert.Contains("asp-controller=\"RecruitmentReports\"", layout);
        Assert.Contains("تقارير التوظيف", layout);
    }
}
