using System.Net;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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

    private sealed record Target(string Key, string Method, string Path, IReadOnlyList<string> Policies, bool Anonymous = false);

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
                targets.Add(new Target($"{action.ControllerName}.{action.MethodInfo.Name}", method, path, policies,
                    endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null));
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

    // --- PLAN-51 (§5, P-7): service accounts ---

    /// <summary>The full scope set each client requests (compose and k8s, inventory 0.5): a call carries all of them.</summary>
    private static readonly Dictionary<string, string> ClientScopes = new(StringComparer.Ordinal)
    {
        [ServiceClientIds.HrContracts] = "hr-core.workflow hr-core.lookup file-storage.references",
        [ServiceClientIds.HrMovements] = "hr-core.lookup",
        [ServiceClientIds.Payroll] = "hr-core.lookup",
        [ServiceClientIds.IdCard] = "hr-core.workflow file-storage.references",
        [ServiceClientIds.RecordsArchive] = "hr-core.workflow file-storage.references",
        [ServiceClientIds.AdminLogistics] = "hr-core.lookup hr-core.workflow file-storage.references",
        [ServiceClientIds.ItInfra] = "hr-core.workflow directory.sync",
        [ServiceClientIds.Compliance] = "hr-core.lookup hr-core.workflow dsar.subject.read dsar.subject.erase file-storage.references",
        [ServiceClientIds.AuthDirectory] = "hr-core.lookup",
    };

    private HttpClient ServiceClient(string clientId)
    {
        var client = host.Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.ServiceClientHeader, clientId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.ServiceScopeHeader, ClientScopes[clientId]);
        return client;
    }

    /// <summary>Every HR.Core call of inventory 0.5 (table A), by the client that makes it in the code.</summary>
    public static TheoryData<string, string, string, string> ClientCalls()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var client in new[] { ServiceClientIds.HrContracts, ServiceClientIds.IdCard, ServiceClientIds.RecordsArchive, ServiceClientIds.AdminLogistics, ServiceClientIds.ItInfra, ServiceClientIds.Compliance })
            data.Add("A1 CodeGenerationClient", client, "POST", "/api/internal/codes/generate");
        foreach (var client in new[] { ServiceClientIds.HrContracts, ServiceClientIds.IdCard, ServiceClientIds.AdminLogistics })
            data.Add("A3 PolicyEnforceClient", client, "POST", "/api/internal/policies/enforce");
        foreach (var client in new[] { ServiceClientIds.HrContracts, ServiceClientIds.AdminLogistics, ServiceClientIds.Payroll, ServiceClientIds.Compliance, ServiceClientIds.AuthDirectory })
            data.Add("A4 HrCoreLookupClient", client, "POST", "/api/lookup/employees/by-ids");
        foreach (var client in new[] { ServiceClientIds.HrContracts, ServiceClientIds.HrMovements })
            data.Add("A6 HrCoreActiveRegimeClient", client, "POST", "/api/internal/service-regime/active-regimes");
        data.Add("A7 HrCoreServiceRegimeClient", ServiceClientIds.Payroll, "POST", "/api/internal/service-regime/payroll-blockers");
        data.Add("A8 HttpApprovalDelegationLookup", ServiceClientIds.HrMovements, "GET", $"/api/internal/approval-delegations/active?userId={Id}");
        data.Add("A9 HttpEmployeeLifecycleGate", ServiceClientIds.HrContracts, "GET", $"/api/internal/employees/{Id}/lifecycle-flags");
        foreach (var client in new[] { ServiceClientIds.IdCard, ServiceClientIds.Compliance, ServiceClientIds.AdminLogistics })
        {
            data.Add("A10/A13 workflow start", client, "POST", "/api/workflow-instances");
            data.Add("A11/A14 workflow cancel", client, "POST", $"/api/workflow-instances/{Id}/cancel");
        }

        foreach (var client in new[] { ServiceClientIds.IdCard, ServiceClientIds.Compliance })
            data.Add("A12 workflow external status", client, "GET", $"/api/workflow-instances/{Id}/external-status");
        data.Add("A15 HttpHrCoreDsarSource", ServiceClientIds.Compliance, "GET", $"/api/internal/dsar-subjects/{Id}");
        data.Add("A16 HttpHrCoreCandidateDsarSource", ServiceClientIds.Compliance, "GET", $"/api/internal/dsar-subjects/candidates/{Id}");
        data.Add("A17 DsarDeletionOrchestrator", ServiceClientIds.Compliance, "POST", $"/api/internal/dsar-subjects/candidates/{Id}/anonymize");
        return data;
    }

    /// <summary>
    /// The two internal endpoints check per request that the client owns what it asks for (a code kind, a policy domain) and answer 403
    /// otherwise, so the call names one it owns, as the client does: a refusal is then the policy's, never the ownership check's.
    /// </summary>
    private static string Body(string clientId, string path)
    {
        if (path.EndsWith("/codes/generate", StringComparison.Ordinal))
        {
            var kind = Enum.GetValues<HRManagement.SharedKernel.CodeGeneration.CodeEntityKind>()
                .First(k => HRManagement.SharedKernel.CodeGeneration.CodeKindOwners.Owns(clientId, k));
            return $$"""{"entityKind":{{(int)kind}},"preview":true}""";
        }

        if (path.EndsWith("/policies/enforce", StringComparison.Ordinal))
        {
            var domain = HRManagement.SharedKernel.Policy.PolicyDomainCatalog.Governed.First(d => d.OwnerClientId == clientId).Domain;
            return $$"""{"domain":"{{domain}}","subjectType":"not-a-subject","action":"none"}""";
        }

        return "{}";
    }

    [Theory]
    [MemberData(nameof(ClientCalls))]
    public async Task Every_service_call_to_hr_core_still_passes_its_policy(string client, string clientId, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = new StringContent(Body(clientId, path), System.Text.Encoding.UTF8, "application/json");
        using var response = await ServiceClient(clientId).SendAsync(request);
        Assert.True(response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized),
            $"{client} as {clientId}: {method} {path} answered {(int)response.StatusCode}");
    }

    /// <summary>
    /// The writes of HR.Core a service account may still reach: the three endpoints of <c>hr-core.workflow</c> (five routes), the internal
    /// routes of the other scopes, and the reads that answer a POST (exports, previews, lookups, evaluation) under the read policies §5's rule
    /// 1 keeps.
    /// </summary>
    private static readonly HashSet<string> ServiceWritable = new(StringComparer.Ordinal)
    {
        "WorkflowExternalInstances.Start", "WorkflowExternalInstances.Cancel",
        "CodeGenerationInternal.Generate", "CodeGenerationInternal.Preview", "PolicyEnforceInternal.Enforce",
        "ServiceRegimeInternal.GetActiveRegimes", "ServiceRegimeInternal.GetPayrollBlockers", "DsarInternal.AnonymizeCandidate",
        "LookupApi.GetEmployeesByIds", "LookupApi.GetOrgUnitsByIds",
        "Reports.ExportEmployeesCsv", "Reports.ExportEmployeesExcel", "Reports.ExportEmployeesPdf", "Reports.ExportEvaluationsCsv",
        "Reports.ExportStatusHistoryCsv", "CodeGeneration.Preview", "Signatures.Verify", "ExportJobs.Cancel",
        // Reads of the policy engine (Policy.Reader keeps the service readers) and the language cookie, which writes no record.
        "Policies.Evaluate", "PolicyTemplates.Simulate", "Culture.SetCulture",
    };

    [Fact]
    public async Task A_service_account_with_the_workflow_scope_writes_nothing_else_in_hr_core()
    {
        // The public candidate and careers endpoints are anonymous by design: anyone reaches them, a service account included.
        var writes = Endpoints((_, _) => true).Where(t => IsWrite(t.Method) && !t.Anonymous).ToList();
        Assert.True(writes.Count > 250, $"only {writes.Count} writes found");

        var failures = new List<string>();
        foreach (var clientId in new[] { ServiceClientIds.IdCard, ServiceClientIds.Compliance, ServiceClientIds.AdminLogistics })
        {
            var client = ServiceClient(clientId);
            foreach (var target in writes)
            {
                using var request = new HttpRequestMessage(new HttpMethod(target.Method), target.Path);
                using var response = await client.SendAsync(request);
                var refused = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized;
                if (!refused && !ServiceWritable.Contains(target.Key))
                    failures.Add($"{clientId}: {target.Method} {target.Path} ({target.Key}) answered {(int)response.StatusCode}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task A_service_account_cannot_create_an_employee()
    {
        var before = await host.WithDbAsync(db => db.Employees.CountAsync());
        using var response = await ServiceClient(ServiceClientIds.AdminLogistics).PostAsync("/Employees/Create",
            new FormUrlEncodedContent([new("FirstName", "x"), new("LastName", "y"), new("NationalId", "12345678901")]));
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized, $"answered {(int)response.StatusCode}");
        Assert.Equal(before, await host.WithDbAsync(db => db.Employees.CountAsync()));
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
