using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Data;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Infrastructure.Data;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// TEN-003: message/event isolation — TenantIdConsumeFilter + stamp interceptor reject cross-tenant writes.
/// </summary>
public class CrossTenantEventLeakageTests
{
    private static ITenantContext Ctx(Guid? tenantId, bool platform = false)
    {
        var m = new Mock<ITenantContext>();
        m.Setup(t => t.TenantId).Returns(tenantId);
        m.Setup(t => t.IsPlatformOperator).Returns(platform);
        if (tenantId.HasValue)
            m.Setup(t => t.RequireTenantId()).Returns(tenantId.Value);
        return m.Object;
    }

    [Fact]
    public async Task ConsumeFilter_sets_flow_from_TenantA_header_clears_after()
    {
        var tenantA = TenantTestFixtures.TenantA;
        var context = new Mock<ConsumeContext<object>>();
        context.Setup(c => c.Headers).Returns(new HeaderBag(tenantA.ToString("D")));

        Guid? observed = Guid.Empty;
        var filter = new TenantIdConsumeFilter<object>();
        var pipe = new Mock<IPipe<ConsumeContext<object>>>();
        pipe.Setup(p => p.Send(It.IsAny<ConsumeContext<object>>()))
            .Returns<ConsumeContext<object>>(_ =>
            {
                observed = TenantIdFlow.Get();
                return Task.CompletedTask;
            });

        await filter.Send(context.Object, pipe.Object);
        Assert.Equal(tenantA, observed);
        Assert.Null(TenantIdFlow.Get());
    }

    [Fact]
    public async Task Consumer_in_TenantB_context_cannot_see_TenantA_rows()
    {
        var tenantA = TenantTestFixtures.TenantA;
        var tenantB = TenantTestFixtures.TenantB;
        var dbName = $"evt-{Guid.NewGuid()}";

        // Simulate consumer processing Tenant A message: stamp + insert under flow A
        TenantIdFlow.Set(tenantA);
        try
        {
            var ctxA = Ctx(tenantA);
            var options = new DbContextOptionsBuilder<HRCoreDbContext>()
                .UseInMemoryDatabase(dbName)
                .AddInterceptors(new TenantIdStampInterceptor(ctxA))
                .Options;
            await using var dbA = new HRCoreDbContext(options, ctxA);
            dbA.Employees.Add(TenantTestFixtures.CreateEmployee(tenantA, null, "A1", "Evt A"));
            await dbA.SaveChangesAsync();
        }
        finally
        {
            TenantIdFlow.Set(null);
        }

        // Consumer mistakenly running under Tenant B must not observe the row
        var ctxB = Ctx(tenantB);
        var optionsB = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new TenantIdStampInterceptor(ctxB))
            .Options;
        await using var dbB = new HRCoreDbContext(optionsB, ctxB);
        Assert.Empty(await dbB.Employees.ToListAsync());
    }

    [Fact]
    public async Task Stamp_interceptor_rejects_insert_when_message_lacks_tenant()
    {
        var empty = Ctx(null);
        var options = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseInMemoryDatabase($"evt-stamp-{Guid.NewGuid()}")
            .AddInterceptors(new TenantIdStampInterceptor(empty))
            .Options;

        await using var db = new ContractsDbContext(options, empty);
        db.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 1, 2),
            DurationDays = 1
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AuthDirectory_consumer_write_is_tenant_stamped_from_flow()
    {
        var tenantA = TenantTestFixtures.TenantA;
        TenantIdFlow.Set(tenantA);
        try
        {
            // TenantContext falls back to TenantIdFlow when no HTTP claim
            var flowCtx = new FlowOnlyTenantContext();
            var options = new DbContextOptionsBuilder<AuthDirectoryDbContext>()
                .UseInMemoryDatabase($"auth-evt-{Guid.NewGuid()}")
                .AddInterceptors(new TenantIdStampInterceptor(flowCtx))
                .Options;

            await using var db = new AuthDirectoryDbContext(options, flowCtx);
            db.AuthUsers.Add(new AuthUser
            {
                Username = "flow-user",
                Email = "flow@test.local",
                PasswordHash = "x",
                IsActive = true
            });
            await db.SaveChangesAsync();

            var user = await db.AuthUsers.SingleAsync();
            Assert.Equal(tenantA, user.TenantId);
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private sealed class FlowOnlyTenantContext : ITenantContext
    {
        public Guid? TenantId => TenantIdFlow.Get();
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => TenantId ?? throw new InvalidOperationException("no tenant");
    }

    private sealed class HeaderBag(string? tenantHeader) : Headers
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

        public T? Get<T>(string key, T? defaultValue = null) where T : struct => defaultValue;

        public IEnumerator<HeaderValue> GetEnumerator()
        {
            foreach (var kv in GetAll())
                yield return new HeaderValue(kv.Key, kv.Value);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
