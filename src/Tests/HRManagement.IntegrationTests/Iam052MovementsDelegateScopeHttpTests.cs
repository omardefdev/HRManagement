using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HR.Movements.Domain.Entities;
using HR.Movements.Infrastructure.Data;
using HR.Movements.Infrastructure.OrgScope;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-52 (P-9): the approval screens a delegation opens (PLAN-50) to a restricted user with an employee. The three employee-only types are
/// central for him (403 on every action, HR.Movements has no employee snapshot, G-MOV-10); on transfers and loans he sees and decides only
/// records with both units in his scope (the kept 403), and the principal's stored scope still applies (PLAN-50's check in the actions).
/// </summary>
[Collection("PostgreSql")]
public sealed class Iam052MovementsDelegateScopeHttpTests(PostgreSqlFixture postgres)
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Guid DelegateEmployee = Guid.Parse("52000000-0000-4000-8000-0000000000e9");
    private static readonly Regex TokenPattern = new("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>A delegation of hr-management-approver in force for every movement type, with the principal's stored scope.</summary>
    private sealed class FixedDelegation : IApprovalDelegationLookup
    {
        public Guid? PrincipalScope { get; set; }

        public Task<IReadOnlyList<ActiveDelegation>> GetActiveForAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ActiveDelegation>>(
            [
                new ActiveDelegation(Guid.NewGuid(), Guid.NewGuid(), null, "principal", HrManagementRoles.HrManagementApprover, null,
                    DateTime.UtcNow.AddDays(10), PrincipalScope),
            ]);
    }

    private sealed record Movements(
        Guid TransferInside, Guid TransferHalf, Guid TransferOutsidePrincipal, Guid LoanInside, Guid LoanHalf,
        Guid Secondment, Guid Promotion, Guid Delegation);

    private static readonly DateOnly Today = new(2026, 3, 1);

    private static async Task<Dictionary<Type, Guid>> SeedAsync(MovementsDbContext db, Guid unit)
    {
        var employee = Guid.NewGuid();
        var records = new List<BaseEntity>
        {
            new TransferRequest { TenantId = Tenant, EmployeeId = employee, FromOrgUnitId = unit, ToOrgUnitId = unit, RequestDate = Today, EffectiveDate = Today },
            new LoanAssignmentRequest { TenantId = Tenant, EmployeeId = employee, LendingOrgUnitId = unit, BorrowingOrgUnitId = unit, StartDate = Today, EndDate = Today.AddMonths(2) },
        };
        db.AddRange(records);
        await db.SaveChangesAsync();
        return records.ToDictionary(r => r.GetType(), r => r.Id);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_restricted_delegate_decides_only_inside_his_scope_and_the_employee_only_types_are_central()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var delegation = new FixedDelegation();
        await using var host = await Iam052PgServiceHost<HR.Movements.Web.Controllers.TransferRequestsController, MovementsDbContext>.StartAsync(
            postgres, "MovementsDb", RegistryOf(), $"{HrManagementRoles.BranchManager},{HrManagementRoles.HrCore}", HrManagementRoles.HrMovements,
            "/TransferRequests", SeedAsync,
            services =>
            {
                services.RemoveAll<IApprovalDelegationLookup>();
                services.AddSingleton<IApprovalDelegationLookup>(delegation);
            });

        // A section C under the delegate's branch, for the principal's narrower scope, and the other records.
        var section = Guid.NewGuid();
        var m = await host.WithDbAsync(async context =>
        {
            var db = (MovementsDbContext)context;
            var branch = await db.OrgUnitSnapshots.AsNoTracking().SingleAsync(u => u.Id == host.OwnUnit);
            db.OrgUnitSnapshots.Add(new OrgUnitSnapshot
            {
                Id = section,
                TenantId = Tenant,
                ParentId = branch.Id,
                Code = "C-52",
                NameAr = "C",
                MaterializedPath = $"{branch.MaterializedPath}{section:D}/"
            });
            var employee = Guid.NewGuid();
            var half = new TransferRequest { TenantId = Tenant, EmployeeId = employee, FromOrgUnitId = host.OwnUnit, ToOrgUnitId = host.ForeignUnit, RequestDate = Today, EffectiveDate = Today };
            var inSection = new TransferRequest { TenantId = Tenant, EmployeeId = employee, FromOrgUnitId = section, ToOrgUnitId = section, RequestDate = Today, EffectiveDate = Today };
            var loanHalf = new LoanAssignmentRequest { TenantId = Tenant, EmployeeId = employee, LendingOrgUnitId = host.ForeignUnit, BorrowingOrgUnitId = host.OwnUnit, StartDate = Today, EndDate = Today.AddMonths(2) };
            var secondment = new SecondmentRequest { TenantId = Tenant, EmployeeId = employee, HostOrg = "host", StartDate = Today, EndDate = Today.AddMonths(2) };
            var promotion = new PromotionRequest { TenantId = Tenant, EmployeeId = employee, FromGrade = 5, ToGrade = 6, TransitionDecisionRef = "ref", EffectiveDate = Today };
            var delegationRequest = new DelegationRequest { TenantId = Tenant, EmployeeId = employee, HostOrg = "host", StartDate = Today, EndDate = Today.AddMonths(2) };
            db.AddRange(half, inSection, loanHalf, secondment, promotion, delegationRequest);
            await db.SaveChangesAsync();
            return new Movements(inSection.Id, half.Id, host.Records[typeof(TransferRequest)].Own, host.Records[typeof(LoanAssignmentRequest)].Own, loanHalf.Id,
                secondment.Id, promotion.Id, delegationRequest.Id);
        });

        using var client = host.RestrictedWith($"{HrManagementRoles.BranchManager},{HrManagementRoles.HrCore}", host.OwnUnit, DelegateEmployee);

        // The three types that carry only an employee: central for him, every action.
        foreach (var (controller, id) in new[] { ("SecondmentRequests", m.Secondment), ("PromotionRequests", m.Promotion), ("DelegationRequests", m.Delegation) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/{controller}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/{controller}/Details/{id}")).StatusCode);
        }

        // Transfers and loans with both units in his scope open; half outside is refused (the kept 403), and the list holds the first only.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/TransferRequests/Details/{m.TransferInside}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/TransferRequests/Details/{m.TransferHalf}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/LoanAssignmentRequests/Details/{m.LoanInside}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/LoanAssignmentRequests/Details/{m.LoanHalf}")).StatusCode);
        var list = await (await client.GetAsync("/TransferRequests")).Content.ReadAsStringAsync();
        Assert.Contains(m.TransferInside.ToString("D"), list, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(m.TransferHalf.ToString("D"), list, StringComparison.OrdinalIgnoreCase);

        // A decision on the half-outside transfer is refused and changes nothing.
        var page = await client.GetAsync($"/TransferRequests/Details/{m.TransferInside}");
        var token = WebUtilityDecode(TokenPattern.Match(await page.Content.ReadAsStringAsync()).Groups[1].Value);
        var refused = await client.PostAsync($"/TransferRequests/Approve/{m.TransferHalf}",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var state = await host.WithDbAsync(async db => (await ((MovementsDbContext)db).TransferRequests.AsNoTracking().SingleAsync(t => t.Id == m.TransferHalf)).Status);
        Assert.Equal(HRManagement.SharedKernel.Enums.ApprovalStatus.Pending, state);

        // The principal's stored scope still applies: with a principal scoped to section C, a transfer inside the delegate's branch but
        // outside C is refused, one inside C still opens.
        delegation.PrincipalScope = section;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/TransferRequests/Details/{m.TransferOutsidePrincipal}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/TransferRequests/Details/{m.TransferInside}")).StatusCode);
    }

    /// <summary>
    /// PLAN-52 (review 1): a delegate with no scope of his own, admitted through a delegation whose principal was branch-scoped, acts within
    /// that principal's scope, so the three employee-only types are central for him too (403, nothing approved); under a central principal's
    /// delegation he reaches them.
    /// </summary>
    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task An_unrestricted_delegate_of_a_branch_scoped_principal_is_refused_the_employee_only_types()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var delegation = new FixedDelegation();
        await using var host = await Iam052PgServiceHost<HR.Movements.Web.Controllers.TransferRequestsController, MovementsDbContext>.StartAsync(
            postgres, "MovementsDb", RegistryOf(), $"{HrManagementRoles.BranchManager},{HrManagementRoles.HrCore}", HrManagementRoles.HrMovements,
            "/TransferRequests", SeedAsync,
            services =>
            {
                services.RemoveAll<IApprovalDelegationLookup>();
                services.AddSingleton<IApprovalDelegationLookup>(delegation);
            });

        var ids = await host.WithDbAsync(async context =>
        {
            var db = (MovementsDbContext)context;
            var employee = Guid.NewGuid();
            var secondment = new SecondmentRequest { TenantId = Tenant, EmployeeId = employee, HostOrg = "host", StartDate = Today, EndDate = Today.AddMonths(2) };
            var promotion = new PromotionRequest { TenantId = Tenant, EmployeeId = employee, FromGrade = 5, ToGrade = 6, TransitionDecisionRef = "ref", EffectiveDate = Today };
            var delegationRequest = new DelegationRequest { TenantId = Tenant, EmployeeId = employee, HostOrg = "host", StartDate = Today, EndDate = Today.AddMonths(2) };
            db.AddRange(secondment, promotion, delegationRequest);
            await db.SaveChangesAsync();
            return new[] { ("SecondmentRequests", secondment.Id), ("PromotionRequests", promotion.Id), ("DelegationRequests", delegationRequest.Id) };
        });

        using var client = host.CentralWith(HrManagementRoles.HrCore, DelegateEmployee);

        delegation.PrincipalScope = null;
        foreach (var (controller, id) in ids)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/{controller}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/{controller}/Details/{id}")).StatusCode);
        }

        async Task<string> StatusesAsync() => await host.WithDbAsync(async context =>
        {
            var db = (MovementsDbContext)context;
            return string.Join("|",
                (await db.SecondmentRequests.AsNoTracking().SingleAsync(r => r.Id == ids[0].Item2)).Status,
                (await db.PromotionRequests.AsNoTracking().SingleAsync(r => r.Id == ids[1].Item2)).Status,
                (await db.DelegationRequests.AsNoTracking().SingleAsync(r => r.Id == ids[2].Item2)).Status);
        });
        var before = await StatusesAsync();

        delegation.PrincipalScope = host.OwnUnit;
        var page = await client.GetAsync("/TransferRequests");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = WebUtilityDecode(TokenPattern.Match(await page.Content.ReadAsStringAsync()).Groups[1].Value);
        foreach (var (controller, id) in ids)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/{controller}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/{controller}/Details/{id}")).StatusCode);
            var refused = await client.PostAsync($"/{controller}/Approve/{id}", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        Assert.Equal(before, await StatusesAsync());
    }

    private static string WebUtilityDecode(string value) => WebUtility.HtmlDecode(value);

    private static OrgScopeKeyRegistry RegistryOf()
    {
        var registry = new OrgScopeKeyRegistry(MovementsOrgScopeKeys.Service);
        MovementsOrgScopeKeys.Configure(registry);
        return registry;
    }
}
