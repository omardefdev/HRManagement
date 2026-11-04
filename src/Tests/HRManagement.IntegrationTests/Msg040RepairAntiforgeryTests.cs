using System.Reflection;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-40 (§6): replay changes state and runs with a gateway session too, so every POST needs an antiforgery token and the
/// token endpoint stays behind the replay policy. The refusal over HTTP is proved by AC-MSG-40g through the gateway.
/// </summary>
public sealed class Msg040RepairAntiforgeryTests
{
    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    [Theory]
    [InlineData(typeof(HR.Core.Web.Controllers.MessagingRepairController))]
    [InlineData(typeof(HR.Movements.Web.Controllers.MessagingRepairController))]
    public void Every_replay_post_requires_an_antiforgery_token(Type controller)
    {
        var posts = Actions(controller).Where(m => m.IsDefined(typeof(HttpPostAttribute))).ToList();

        Assert.Equal(2, posts.Count);
        Assert.All(posts, m => Assert.True(m.IsDefined(typeof(ValidateAntiForgeryTokenAttribute)), m.Name));
    }

    [Theory]
    [InlineData(typeof(HR.Core.Web.Controllers.MessagingRepairController))]
    [InlineData(typeof(HR.Movements.Web.Controllers.MessagingRepairController))]
    public void The_token_endpoint_stays_behind_the_replay_policy(Type controller)
    {
        Assert.Equal(HrManagementPolicies.MessagingRepair, controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy);
        Assert.Single(Actions(controller), m => m.IsDefined(typeof(HttpGetAttribute)));
        Assert.DoesNotContain(Actions(controller), m => m.IsDefined(typeof(AllowAnonymousAttribute)));
    }
}
