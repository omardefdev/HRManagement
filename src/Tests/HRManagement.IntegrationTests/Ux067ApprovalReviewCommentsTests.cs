using System.Net;
using System.Text.RegularExpressions;
using HR.Core.Domain.Entities;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-67 (G-UX-01, P-4): the reviewer's comments reach the approval record with no inline code. The review page has one form that
/// holds the comments field and both decisions (the reject button posts to its action with <c>formaction</c>); before PLAN-67 two
/// <c>onclick</c> handlers copied the comments into hidden fields, and an enforced CSP blocked them, so every decision was saved without
/// its comments. The test reads the page as a browser would: the form's own action, its buttons' targets and its token.
/// </summary>
[Collection(SharedHrCoreHost.Name)]
public sealed class Ux067ApprovalReviewCommentsTests(Hcm025ScopeAndPolicyTestHost host)
{
    private static readonly Regex Form = new("<form[^>]*data-testid=\"approval-review-form\"[^>]*>(?<body>.*?)</form>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex Action = new("action=\"(?<a>[^\"]+)\"", RegexOptions.CultureInvariant);
    private static readonly Regex RejectButton = new("<button[^>]*data-testid=\"approval-reject\"[^>]*formaction=\"(?<a>[^\"]+)\"", RegexOptions.CultureInvariant);
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"(?<t>[^\"]+)\"", RegexOptions.CultureInvariant);

    private Task<Guid> SeedPendingAsync() =>
        host.WithDbAsync(async db =>
        {
            var request = new ApprovalRequest
            {
                TenantId = TenantDefaults.DefaultTenantId,
                EntityType = "Ux067Check",
                EntityId = Guid.NewGuid(),
                Description = "طلب اعتماد للتحقق من ملاحظات المراجع",
                Status = ApprovalStatus.Pending,
                CurrentLevel = 1
            };
            request.Steps.Add(new ApprovalStep { Level = 1, RoleRequired = "", Status = ApprovalStatus.Pending });
            db.ApprovalRequests.Add(request);
            await db.SaveChangesAsync();
            return request.Id;
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_reviewer_comments_reach_the_record(bool reject)
    {
        var id = await SeedPendingAsync();
        using var client = host.Client(Guid.NewGuid(), "hr-admin");
        var response = await client.GetAsync($"/Approvals/Review/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("onclick=", page, StringComparison.OrdinalIgnoreCase);
        var form = Form.Match(page);
        Assert.True(form.Success, "The review page has no single review form.");
        var formTag = page.Substring(form.Index, page.IndexOf('>', form.Index) - form.Index);
        Assert.Contains("name=\"reviewComments\"", form.Groups["body"].Value, StringComparison.Ordinal);
        var target = reject
            ? WebUtility.HtmlDecode(RejectButton.Match(form.Groups["body"].Value).Groups["a"].Value)
            : WebUtility.HtmlDecode(Action.Match(formTag).Groups["a"].Value);
        Assert.False(string.IsNullOrEmpty(target), "The decision's target was not found on the page.");
        var token = WebUtility.HtmlDecode(Token.Match(form.Groups["body"].Value).Groups["t"].Value);

        var comments = reject ? "المستندات ناقصة، يُعاد بعد استكمالها" : "مطابق للملاك المعتمد";
        var post = await Hcm025ScopeAndPolicyTestHost.PostAsync(client, target, token, ("id", id.ToString("D")), ("reviewComments", comments));
        Assert.True(post.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found, $"POST {target} answered {(int)post.StatusCode}");

        var saved = await host.WithDbAsync(db => db.ApprovalRequests.AsNoTracking().SingleAsync(r => r.Id == id));
        Assert.Equal(reject ? ApprovalStatus.Rejected : ApprovalStatus.Approved, saved.Status);
        Assert.Equal(comments, saved.ReviewComments);
    }
}
