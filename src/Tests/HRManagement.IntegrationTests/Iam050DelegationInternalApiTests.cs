using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-50 (P-5): the internal endpoint on the in-process HR.Core host. An interactive user is refused (403), whatever his roles;
/// <c>svc-hr-movements</c> with <c>hr-core.lookup</c> gets the delegations in force of the employee it names; another client, or the
/// right client without the scope, is refused. Row security is proved on PostgreSQL in <see cref="Iam050DelegationPostgreSqlTests"/>.
/// </summary>
public sealed class Iam050DelegationInternalApiTests(Hcm025ScopeAndPolicyTestHost host) : IClassFixture<Hcm025ScopeAndPolicyTestHost>
{
    private const string Path = "/api/internal/approval-delegations/active";

    private HttpClient Service(string clientId, string? scope)
    {
        var client = host.Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.ServiceClientHeader, clientId);
        if (scope is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.ServiceScopeHeader, scope);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, TenantDefaults.DefaultTenantId.ToString("D"));
        return client;
    }

    private async Task<Guid> SeedAsync(Guid employee, bool revoked = false) =>
        await host.WithDbAsync(async db =>
        {
            var delegation = new ApprovalDelegation
            {
                TenantId = TenantDefaults.DefaultTenantId,
                PrincipalId = Guid.NewGuid(),
                PrincipalName = "iam050-principal",
                DelegateEmployeeId = employee,
                RoleScope = HrManagementRoles.HrManagementApprover,
                EntityTypeScope = "TransferRequest",
                ActiveFrom = DateTime.UtcNow.AddDays(-1),
                ActiveUntil = DateTime.UtcNow.AddDays(3),
                Reason = "iam050",
                IsRevoked = revoked
            };
            db.ApprovalDelegations.Add(delegation);
            await db.SaveChangesAsync();
            return delegation.Id;
        });

    [Fact]
    public async Task The_movement_service_reads_the_delegations_in_force_of_an_employee()
    {
        var employee = Guid.NewGuid();
        var active = await SeedAsync(employee);
        await SeedAsync(employee, revoked: true);

        var response = await Service(ServiceClientIds.HrMovements, ServiceScopes.HrCoreLookup).GetAsync($"{Path}?employeeId={employee:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApprovalDelegationsResponse>();
        var delegation = Assert.Single(body!.Delegations);
        Assert.Equal(active, delegation.Id);
        Assert.Equal(HrManagementRoles.HrManagementApprover, delegation.Role);
        Assert.Equal("TransferRequest", delegation.EntityTypeScope);
        Assert.Equal("iam050-principal", delegation.PrincipalName);
    }

    [Fact]
    public async Task An_interactive_user_another_client_and_a_client_without_the_scope_are_refused()
    {
        var employee = Guid.NewGuid();
        await SeedAsync(employee);
        var url = $"{Path}?employeeId={employee:D}";

        var user = host.Client(Guid.NewGuid(), $"{HrManagementRoles.Admin},{HrManagementRoles.HrCore},{HrManagementRoles.HrManagementApprover}");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Service(ServiceClientIds.HrContracts, ServiceScopes.HrCoreLookup).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Service(ServiceClientIds.HrMovements, null).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Service(ServiceClientIds.HrMovements, ServiceScopes.HrCoreWorkflow).GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task A_call_that_names_no_one_gets_nothing()
    {
        var response = await Service(ServiceClientIds.HrMovements, ServiceScopes.HrCoreLookup).GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<ApprovalDelegationsResponse>())!.Delegations);
    }
}
