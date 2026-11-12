using System.Net;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-51 (§4, Acceptance «خارج القبول الحي»): straight to HR.Core in process, without the gateway, as PLAN-25's host does. A user who holds
/// <c>hr-employee</c> and the supervisory read role gets 403 on every one of the 42 writes that HR.Core's old list let any module role
/// reach (generated from that list), while <c>hr-core</c> passes their policy; and no interactive user, whatever his roles, reaches an
/// <c>api/internal</c> route.
/// </summary>
public sealed class Iam051DirectServiceWriteTests(Hcm025ScopeAndPolicyTestHost host) : IClassFixture<Hcm025ScopeAndPolicyTestHost>
{
    /// <summary>The 42 rows of the deleted <c>tests/architecture/hrcore-post-policy-allowlist.txt</c> (PLAN-25), as they stood at P0.</summary>
    public static readonly string[] FormerActions =
    [
        "ApprovedCVs.Create", "ApprovedCVs.Delete", "ApprovedCVs.Edit", "ApprovedCVs.FinalizeApproval", "CareerPaths.AddStep",
        "CareerPaths.Create", "CareerPaths.Delete", "CareerPaths.RemoveStep", "Contacts.Create", "Contacts.Delete", "Contacts.Edit",
        "DailyAttendanceRecords.BulkEntry", "DailyAttendanceRecords.ClosePeriod", "DailyAttendanceRecords.Create",
        "DailyAttendanceRecords.Delete", "DailyAttendanceRecords.Edit", "DiwanCorrespondences.Create", "Documents.Create",
        "Documents.Delete", "Documents.Edit", "EmployeeProfiles.Save", "ExportJobs.Cancel", "FieldInspectionVisits.Create",
        "HrStatisticReports.Create", "KeyPositions.Create", "KeyPositions.Delete", "KeyPositions.Edit", "Qualifications.Create",
        "Qualifications.Delete", "Qualifications.Edit", "Rosters.AddAssignment", "Rosters.Create", "Rosters.Delete",
        "Rosters.RemoveAssignment", "Shifts.Create", "Shifts.Delete", "Shifts.Edit", "Signatures.Verify", "SuccessionPlans.AddCandidate",
        "SuccessionPlans.Create", "SuccessionPlans.Delete", "SuccessionPlans.RemoveCandidate",
    ];

    /// <summary>The two that are reads (plan §4): the signature check and cancelling one's own export, under <c>HrCore.ReadOnly</c>.</summary>
    public static readonly string[] ReadActions = ["Signatures.Verify", "ExportJobs.Cancel"];

    private const string Id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private sealed record Target(string Key, string Method, string Path, IReadOnlyList<string> Policies);

    private IReadOnlyList<Target> Endpoints(Func<ControllerActionDescriptor, RouteEndpoint, bool> predicate)
    {
        var links = host.Host.Services.GetRequiredService<LinkGenerator>();
        var targets = new List<Target>();
        foreach (var endpoint in host.Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action is null || !predicate(action, endpoint))
                continue;
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var values = new RouteValueDictionary { ["controller"] = action.ControllerName, ["action"] = action.ActionName };
            foreach (var parameter in endpoint.RoutePattern.Parameters.Where(p => p.Name is not ("controller" or "action")))
                values[parameter.Name] = Id;
            var path = links.GetPathByRouteValues(routeName: null, values) ?? "/" + endpoint.RoutePattern.RawText;
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>().ToList();
            foreach (var method in methods)
                targets.Add(new Target($"{action.ControllerName}.{action.MethodInfo.Name}", method, path, policies));
        }

        return targets;
    }

    private static bool IsWrite(string method) => method is "POST" or "PUT" or "PATCH" or "DELETE";

    [Fact]
    public async Task A_supervisory_employee_gets_403_on_every_former_write_and_hr_core_passes_its_policy()
    {
        var former = FormerActions.ToHashSet(StringComparer.Ordinal);
        var targets = Endpoints((action, _) => former.Contains($"{action.ControllerName}.{action.MethodInfo.Name}"))
            .Where(t => IsWrite(t.Method))
            .ToList();
        Assert.Equal(former.OrderBy(k => k, StringComparer.Ordinal), targets.Select(t => t.Key).Distinct().OrderBy(k => k, StringComparer.Ordinal));

        var reader = host.Client(Guid.NewGuid(), $"{HrManagementRoles.HrEmployee},{HrManagementRoles.HrCoreSupervisory}");
        var writer = host.Client(Guid.NewGuid(), HrManagementRoles.HrCore);
        var failures = new List<string>();
        foreach (var target in targets)
        {
            if (ReadActions.Contains(target.Key))
            {
                if (!target.Policies.Contains(HrManagementPolicies.HrCoreReadOnly))
                    failures.Add($"{target.Key}: expected HrCore.ReadOnly, has {string.Join(", ", target.Policies)}");
                continue;
            }

            if (!target.Policies.Contains(HrManagementPolicies.HrCoreWrite))
                failures.Add($"{target.Key}: names no HrCore.Write ({string.Join(", ", target.Policies)})");

            using var refused = await reader.SendAsync(new HttpRequestMessage(new HttpMethod(target.Method), target.Path));
            if (refused.StatusCode != HttpStatusCode.Forbidden)
                failures.Add($"{target.Method} {target.Path} as a supervisory employee: {(int)refused.StatusCode}, expected 403");

            using var admitted = await writer.SendAsync(new HttpRequestMessage(new HttpMethod(target.Method), target.Path));
            if (admitted.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                failures.Add($"{target.Method} {target.Path} as hr-core: {(int)admitted.StatusCode}, expected its policy to pass");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task No_interactive_user_reaches_an_internal_route()
    {
        var targets = Endpoints((_, endpoint) =>
            endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("api/internal", StringComparison.OrdinalIgnoreCase) == true);
        Assert.True(targets.Count >= 8, $"only {targets.Count} internal routes found");

        var everyone = host.Client(Guid.NewGuid(), string.Join(',', HrManagementRoles.All));
        var failures = new List<string>();
        foreach (var target in targets)
        {
            using var response = await everyone.SendAsync(new HttpRequestMessage(new HttpMethod(target.Method), target.Path));
            if (response.StatusCode != HttpStatusCode.Forbidden)
                failures.Add($"{target.Method} {target.Path}: {(int)response.StatusCode}, expected 403");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
