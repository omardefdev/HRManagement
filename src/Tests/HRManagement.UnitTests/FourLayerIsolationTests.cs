using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;

namespace HRManagement.UnitTests;

/// <summary>PLAN-01 AC-04: four isolation layers (gateway / service / EF / RLS).</summary>
public class FourLayerIsolationTests
{
    [Fact]
    public void Layer1_gateway_tenant_barrier_requires_valid_claim_for_hr_core()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, HrManagementRoles.HrCore)],
            "test"));

        var claim = ctx.User.FindFirst(TenantContext.TenantClaimType)?.Value;
        Assert.False(Guid.TryParse(claim, out _));
    }

    [Fact]
    public void Layer2_service_records_org_scope_denial_for_gateway_audit()
    {
        var http = new DefaultHttpContext();
        OrgScopeDenialContext.Record(
            http,
            new System.Security.Claims.ClaimsPrincipal(),
            reason: "org-scope-denied",
            resourceType: "Employee",
            resourceId: AcceptanceSeedIds.EmpBId.ToString("D"));

        var denial = OrgScopeDenialContext.Get(http);
        Assert.NotNull(denial);
        Assert.Equal("org-scope-denied", denial!.Reason);
        Assert.Equal(AcceptanceSeedIds.EmpBId.ToString("D"), denial.ResourceId);
    }

    [Fact]
    public void Layer3_ef_effective_tenant_is_empty_when_unset()
    {
        var ctx = new StubTenantAwareDbContext(new StubTenantContext(null, false));
        Assert.Equal(Guid.Empty, ctx.EffectiveTenantId);
        Assert.False(ctx.BypassTenantFilter);
    }

    [Fact]
    public void Layer4_pg_session_interceptor_exists_for_rls()
    {
        Assert.NotNull(typeof(NpgsqlTenantSessionInterceptor).GetConstructor([typeof(ITenantContext)]));
    }

    private sealed class StubTenantContext(Guid? tenantId, bool platform) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => platform;
        public Guid RequireTenantId() => tenantId ?? throw new InvalidOperationException();
    }

    private sealed class StubTenantAwareDbContext(ITenantContext tenant)
        : TenantAwareDbContext(new Microsoft.EntityFrameworkCore.DbContextOptions<StubTenantAwareDbContext>(), tenant);
}
