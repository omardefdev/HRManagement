using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace HRManagement.UnitTests;

public class TenantIdFlowTests
{
    [Fact]
    public void Set_and_Get_round_trip()
    {
        var id = Guid.NewGuid();
        TenantIdFlow.Set(id);
        Assert.Equal(id, TenantIdFlow.Get());
        TenantIdFlow.Set(null);
        Assert.Null(TenantIdFlow.Get());
    }

    [Fact]
    public async Task AsyncLocal_flows_across_await()
    {
        var id = Guid.NewGuid();
        TenantIdFlow.Set(id);
        await Task.Yield();
        Assert.Equal(id, TenantIdFlow.Get());
        TenantIdFlow.Set(null);
    }
}

public class TenantContextTests
{
    [Fact]
    public void TenantId_from_claim()
    {
        var tenantId = Guid.NewGuid();
        var accessor = CreateAccessor(tenantId, isPlatformAdmin: false);
        var ctx = new TenantContext(accessor);

        Assert.Equal(tenantId, ctx.TenantId);
        Assert.False(ctx.IsPlatformOperator);
        Assert.Equal(tenantId, ctx.RequireTenantId());
    }

    [Fact]
    public void TenantId_falls_back_to_flow_when_no_claim()
    {
        var tenantId = Guid.NewGuid();
        TenantIdFlow.Set(tenantId);
        try
        {
            var accessor = CreateAccessor(tenantId: null, isPlatformAdmin: false);
            var ctx = new TenantContext(accessor);
            Assert.Equal(tenantId, ctx.TenantId);
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    [Fact]
    public void IsPlatformOperator_true_for_platform_admin_role()
    {
        var accessor = CreateAccessor(Guid.NewGuid(), isPlatformAdmin: true);
        var ctx = new TenantContext(accessor);
        Assert.True(ctx.IsPlatformOperator);
    }

    [Fact]
    public void RequireTenantId_throws_when_missing()
    {
        TenantIdFlow.Set(null);
        var accessor = CreateAccessor(tenantId: null, isPlatformAdmin: false);
        var ctx = new TenantContext(accessor);
        Assert.Throws<InvalidOperationException>(() => ctx.RequireTenantId());
    }

    [Fact]
    public void Platform_admin_without_impersonation_has_null_tenant()
    {
        var accessor = CreateAccessor(tenantId: null, isPlatformAdmin: true);
        var ctx = new TenantContext(accessor);
        Assert.Null(ctx.TenantId);
        Assert.True(ctx.IsPlatformOperator);
        Assert.False(((ITenantContext)ctx).IsPlatformImpersonating);
    }

    [Fact]
    public void Platform_admin_with_session_item_resolves_tenant()
    {
        var targetTenant = Guid.NewGuid();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, HrManagementRoles.PlatformAdmin)], "test"))
        };
        http.Items[PlatformTenantSessionMiddleware.ItemKey] = new PlatformTenantSessionEntry(
            targetTenant, "audit", DateTimeOffset.UtcNow, null);

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(http);
        var ctx = new TenantContext(accessor.Object);

        Assert.Equal(targetTenant, ctx.TenantId);
        Assert.True(((ITenantContext)ctx).IsPlatformImpersonating);
    }

    private static IHttpContextAccessor CreateAccessor(Guid? tenantId, bool isPlatformAdmin)
    {
        var claims = new List<Claim>();
        if (tenantId.HasValue)
            claims.Add(new Claim(TenantContext.TenantClaimType, tenantId.Value.ToString("D")));
        if (isPlatformAdmin)
            claims.Add(new Claim(ClaimTypes.Role, HrManagementRoles.PlatformAdmin));

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(http);
        return accessor.Object;
    }
}

public class TenantIdMiddlewareTests
{
    [Fact]
    public async Task Sets_flow_and_items_from_claim_then_clears()
    {
        var tenantId = Guid.NewGuid();
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(TenantContext.TenantClaimType, tenantId.ToString("D"))], "test"))
        };

        Guid? observedDuring = null;
        var mw = new TenantIdMiddleware(async http =>
        {
            observedDuring = TenantIdFlow.Get();
            Assert.Equal(tenantId, http.Items[TenantIdMiddleware.ItemKey]);
            await Task.CompletedTask;
        });

        await mw.InvokeAsync(ctx);

        Assert.Equal(tenantId, observedDuring);
        Assert.Null(TenantIdFlow.Get());
    }

    [Fact]
    public async Task Missing_claim_sets_null_flow()
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity("test"))
        };

        Guid? observed = Guid.NewGuid();
        var mw = new TenantIdMiddleware(_ =>
        {
            observed = TenantIdFlow.Get();
            return Task.CompletedTask;
        });

        await mw.InvokeAsync(ctx);

        Assert.Null(observed);
        Assert.Null(TenantIdFlow.Get());
    }
}

public class TenantIdConsumeFilterTests
{
    [Fact]
    public async Task Sets_flow_from_header_then_clears()
    {
        var tenantId = Guid.NewGuid();
        var context = new Mock<ConsumeContext<object>>();
        context.Setup(c => c.Headers).Returns(new StubHeaders(tenantId.ToString("D")));

        Guid? observed = null;
        var filter = new TenantIdConsumeFilter<object>();
        var pipe = new Mock<IPipe<ConsumeContext<object>>>();
        pipe.Setup(p => p.Send(It.IsAny<ConsumeContext<object>>()))
            .Returns<ConsumeContext<object>>(_ =>
            {
                observed = TenantIdFlow.Get();
                return Task.CompletedTask;
            });

        await filter.Send(context.Object, pipe.Object);

        Assert.Equal(tenantId, observed);
        Assert.Null(TenantIdFlow.Get());
    }

    private sealed class StubHeaders(string? tenantHeader) : Headers
    {
        public IEnumerable<KeyValuePair<string, object>> GetAll()
        {
            if (tenantHeader is not null)
                yield return new KeyValuePair<string, object>(TenantIdMiddleware.HeaderName, tenantHeader);
        }

        public bool TryGetHeader(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out object? value)
        {
            if (key == TenantIdMiddleware.HeaderName && tenantHeader is not null)
            {
                value = tenantHeader;
                return true;
            }

            value = null;
            return false;
        }

        public T? Get<T>(string key, T? defaultValue = null) where T : class
        {
            if (key == TenantIdMiddleware.HeaderName && tenantHeader is T typed)
                return typed;
            return defaultValue;
        }

        public T? Get<T>(string key, T? defaultValue = null) where T : struct
        {
            return defaultValue;
        }

        public IEnumerator<HeaderValue> GetEnumerator()
        {
            foreach (var kv in GetAll())
                yield return new HeaderValue(kv.Key, kv.Value);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public class TenantIdStampInterceptorTests
{
    private sealed class StampEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class StampDbContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<StampEntity> Entities => Set<StampEntity>();
        public DbSet<EntityAuditLog> AuditLogs => Set<EntityAuditLog>();
    }

    [Fact]
    public void Stamps_TenantId_on_Added_when_context_present()
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.TenantId).Returns(tenantId);

        var options = new DbContextOptionsBuilder<StampDbContext>()
            .UseInMemoryDatabase($"Stamp_{Guid.NewGuid()}")
            .AddInterceptors(new TenantIdStampInterceptor(tenantCtx.Object))
            .Options;

        using var db = new StampDbContext(options);
        var entity = new StampEntity { Name = "a" };
        db.Entities.Add(entity);
        db.SaveChanges();

        Assert.Equal(tenantId, entity.TenantId);
    }

    [Fact]
    public void Rejects_insert_carrying_another_tenants_id()
    {
        // TenantId is a public settable property on BaseEntity, so any controller that model-binds
        // an entity lets a client post another agency's tenant id. Honouring that value wrote rows
        // into a tenant the request had no claim to.
        var contextTenant = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.TenantId).Returns(contextTenant);
        tenantCtx.Setup(t => t.IsPlatformOperator).Returns(false);

        var options = new DbContextOptionsBuilder<StampDbContext>()
            .UseInMemoryDatabase($"Stamp_{Guid.NewGuid()}")
            .AddInterceptors(new TenantIdStampInterceptor(tenantCtx.Object))
            .Options;

        using var db = new StampDbContext(options);
        db.Entities.Add(new StampEntity { Name = "a", TenantId = foreignTenant });

        Assert.Throws<UnauthorizedAccessException>(() => db.SaveChanges());
    }

    [Fact]
    public void Accepts_insert_carrying_the_ambient_tenant_id()
    {
        var contextTenant = Guid.NewGuid();
        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.TenantId).Returns(contextTenant);
        tenantCtx.Setup(t => t.IsPlatformOperator).Returns(false);

        var options = new DbContextOptionsBuilder<StampDbContext>()
            .UseInMemoryDatabase($"Stamp_{Guid.NewGuid()}")
            .AddInterceptors(new TenantIdStampInterceptor(tenantCtx.Object))
            .Options;

        using var db = new StampDbContext(options);
        var entity = new StampEntity { Name = "a", TenantId = contextTenant };
        db.Entities.Add(entity);
        db.SaveChanges();

        Assert.Equal(contextTenant, entity.TenantId);
    }

    [Fact]
    public void Platform_operator_may_write_for_an_explicit_tenant()
    {
        var contextTenant = Guid.NewGuid();
        var targetTenant = Guid.NewGuid();
        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.TenantId).Returns(contextTenant);
        tenantCtx.Setup(t => t.IsPlatformOperator).Returns(true);

        var options = new DbContextOptionsBuilder<StampDbContext>()
            .UseInMemoryDatabase($"Stamp_{Guid.NewGuid()}")
            .AddInterceptors(new TenantIdStampInterceptor(tenantCtx.Object))
            .Options;

        using var db = new StampDbContext(options);
        var entity = new StampEntity { Name = "a", TenantId = targetTenant };
        db.Entities.Add(entity);
        db.SaveChanges();

        Assert.Equal(targetTenant, entity.TenantId);
    }

    [Fact]
    public void Rejects_insert_when_no_tenant_context()
    {
        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.TenantId).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<StampDbContext>()
            .UseInMemoryDatabase($"Stamp_{Guid.NewGuid()}")
            .AddInterceptors(new TenantIdStampInterceptor(tenantCtx.Object))
            .Options;

        using var db = new StampDbContext(options);
        db.Entities.Add(new StampEntity { Name = "a" });
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public void Background_audit_inherits_explicit_entity_tenant()
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.TenantId).Returns((Guid?)null);
        var httpContext = new Mock<IHttpContextAccessor>();

        var options = new DbContextOptionsBuilder<StampDbContext>()
            .UseInMemoryDatabase($"Stamp_{Guid.NewGuid()}")
            .AddInterceptors(
                new AuditSaveChangesInterceptor(httpContext.Object),
                new TenantIdStampInterceptor(tenantCtx.Object))
            .Options;

        using var db = new StampDbContext(options);
        var entity = new StampEntity { Name = "background", TenantId = tenantId };
        db.Entities.Add(entity);
        db.SaveChanges();

        var audit = Assert.Single(db.AuditLogs.Where(x => x.EntityId == entity.Id));
        Assert.Equal(tenantId, audit.TenantId);
    }

    [Fact]
    public void Request_audit_inherits_ambient_tenant_before_entity_stamp_runs()
    {
        var tenantId = Guid.NewGuid();
        var tenantCtx = new Mock<ITenantContext>();
        tenantCtx.Setup(t => t.TenantId).Returns(tenantId);
        tenantCtx.Setup(t => t.IsPlatformOperator).Returns(false);
        var httpContext = new Mock<IHttpContextAccessor>();

        var options = new DbContextOptionsBuilder<StampDbContext>()
            .UseInMemoryDatabase($"Stamp_{Guid.NewGuid()}")
            .AddInterceptors(
                new AuditSaveChangesInterceptor(httpContext.Object, tenantCtx.Object),
                new TenantIdStampInterceptor(tenantCtx.Object))
            .Options;

        using var db = new StampDbContext(options);
        var entity = new StampEntity { Name = "request" };
        db.Entities.Add(entity);
        db.SaveChanges();

        var audit = Assert.Single(db.AuditLogs.Where(x => x.EntityId == entity.Id));
        Assert.Equal(tenantId, entity.TenantId);
        Assert.Equal(tenantId, audit.TenantId);
    }
}

public class TenantKeycloakRoleMappingTests
{
    [Fact]
    public void MapRealmRolesFromAccessToken_maps_tenant_id_claim()
    {
        var tenantId = Guid.NewGuid();
        var token = CreateToken($$"""{"sub":"user1","tenant_id":"{{tenantId:D}}"}""");

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "user1")], "Test");
        var principal = new ClaimsPrincipal(identity);

        KeycloakRoleMapping.MapRealmRolesFromAccessToken(principal, token);

        Assert.Equal(tenantId.ToString("D"), principal.FindFirst(TenantContext.TenantClaimType)?.Value);
    }

    private static string CreateToken(string payloadJson)
    {
        var header = Base64Url(System.Text.Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        var payload = Base64Url(System.Text.Encoding.UTF8.GetBytes(payloadJson));
        return $"{header}.{payload}.";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
