using System.Net;
using HR.Core.Domain.Entities;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using static HRManagement.IntegrationTests.Hcm025ScopeAndPolicyTestHost;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-25 (G-15, AC-HCM-25f): writes on legacy public postings and internal vacancies need an explicit policy —
/// <c>hr-management-approver</c> (which the global Module.HrCore filter lets in) is refused with 403; hr-core may edit,
/// the closing-date change is audited and cannot close a posting with applications retroactively; delete is admin-only.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Hcm025PostingPoliciesTests(Hcm025ScopeAndPolicyTestHost fx) : IClassFixture<Hcm025ScopeAndPolicyTestHost>
{
    private static readonly Guid ApproverUser = Guid.Parse("25250000-0000-4000-8000-0000000000a5");
    private static readonly Guid HrUser = Guid.Parse("25250000-0000-4000-8000-0000000000c5");

    private Task<PublicJobPosting> SeedPostingAsync(bool withApplication) =>
        fx.WithDbAsync(async db =>
        {
            var posting = new PublicJobPosting
            {
                TenantId = TenantDefaults.DefaultTenantId,
                Title = "إعلان قديم",
                Entity = "جهة",
                IsPublished = true,
                ClosingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10)
            };
            db.PublicJobPostings.Add(posting);
            if (withApplication)
                db.CitizenApplications.Add(new CitizenApplication
                {
                    TenantId = TenantDefaults.DefaultTenantId,
                    PublicJobPostingId = posting.Id,
                    ReferenceNumber = $"CA-P25-{Guid.NewGuid():N}"[..14],
                    FullName = "مواطن",
                    NationalId = "01234567890"
                });
            await db.SaveChangesAsync();
            return posting;
        });

    private static (string, string)[] EditFields(PublicJobPosting posting, DateOnly closing) =>
    [
        ("Id", posting.Id.ToString("D")),
        ("Title", posting.Title),
        ("Entity", posting.Entity ?? "جهة"),
        ("ClosingDate", closing.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
        ("IsPublished", "true")
    ];

    [Fact]
    public async Task Management_approver_cannot_edit_or_delete_a_posting()
    {
        var posting = await SeedPostingAsync(withApplication: false);
        using var approver = fx.Client(ApproverUser, HrManagementRoles.HrManagementApprover);
        var token = await TokenAsync(approver, $"/PublicJobPostings/Details/{posting.Id:D}");

        var edit = await PostAsync(approver, $"/PublicJobPostings/Edit/{posting.Id:D}", token,
            EditFields(posting, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)));
        var delete = await PostAsync(approver, $"/PublicJobPostings/Delete/{posting.Id:D}", token);

        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.GetAsync($"/PublicJobPostings/Edit/{posting.Id:D}")).StatusCode);
        Assert.True(await fx.WithDbAsync(db => db.PublicJobPostings.AnyAsync(p => p.Id == posting.Id)));
    }

    [Fact]
    public async Task Hr_core_edit_audits_the_closing_date_and_refuses_a_retroactive_close_with_applications()
    {
        var posting = await SeedPostingAsync(withApplication: true);
        using var hr = fx.Client(HrUser, HrManagementRoles.HrCore);
        var token = await TokenAsync(hr, $"/PublicJobPostings/Edit/{posting.Id:D}");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var retroactive = await PostAsync(hr, $"/PublicJobPostings/Edit/{posting.Id:D}", token, EditFields(posting, today.AddDays(-1)));
        var body = WebUtility.HtmlDecode(await retroactive.Content.ReadAsStringAsync());
        var extended = await PostAsync(hr, $"/PublicJobPostings/Edit/{posting.Id:D}", token, EditFields(posting, today.AddDays(20)));

        Assert.Equal(HttpStatusCode.OK, retroactive.StatusCode);   // the form is shown again with the error
        Assert.Contains(LegacyPostingClosingDateRules.ShortenedBelowTodayMessage, body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, extended.StatusCode);
        Assert.Equal(today.AddDays(20), await fx.WithDbAsync(db =>
            db.PublicJobPostings.AsNoTracking().Where(p => p.Id == posting.Id).Select(p => p.ClosingDate).SingleAsync()));
        Assert.Equal(1, await fx.WithDbAsync(db => db.AuditLogs.CountAsync(a =>
            a.EntityId == posting.Id && a.Action == LegacyPostingClosingDateRules.AuditAction && a.UserId == HrUser)));
    }

    [Fact]
    public async Task Hr_core_cannot_delete_but_admin_can()
    {
        var posting = await SeedPostingAsync(withApplication: false);
        using var hr = fx.Client(HrUser, HrManagementRoles.HrCore);
        using var admin = fx.Client(Guid.NewGuid(), HrManagementRoles.Admin);

        var hrDelete = await PostAsync(hr, $"/PublicJobPostings/Delete/{posting.Id:D}",
            await TokenAsync(hr, $"/PublicJobPostings/Details/{posting.Id:D}"));
        var adminDelete = await PostAsync(admin, $"/PublicJobPostings/Delete/{posting.Id:D}",
            await TokenAsync(admin, $"/PublicJobPostings/Details/{posting.Id:D}"));

        Assert.Equal(HttpStatusCode.Forbidden, hrDelete.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, adminDelete.StatusCode);
    }
}
