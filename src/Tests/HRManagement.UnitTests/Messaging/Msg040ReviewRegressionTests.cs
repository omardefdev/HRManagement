using System.Data;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests.Messaging;

/// <summary>PLAN-40 review findings, fixed and held here.</summary>
public sealed class Msg040ReviewRegressionTests
{
    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }

    [Fact]
    public async Task IEmployeeCreated_carries_the_tenant_the_save_stamps()
    {
        var tenant = Guid.NewGuid();
        var context = new FixedTenant(tenant);
        var probe = new OutboxOrderProbe();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(probe.Interceptor, new TenantIdStampInterceptor(context))
            .Options;
        await using var db = new HRCoreDbContext(options, context);
        var employee = new Employee
        {
            FirstName = "موظف", LastName = "اختبار", FullName = "موظف اختبار", NationalId = "N" + Guid.NewGuid().ToString("N")[..10],
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), Gender = Gender.Male, CurrentStatus = EmploymentStatus.Active
        };
        db.Employees.Add(employee);
        Assert.Equal(Guid.Empty, employee.TenantId);

        await new EmployeeIntegrationService(db, probe.Publish).CreateEmployeeWithEventAsync(employee);

        var message = Assert.Single(probe.Messages);
        Assert.Equal(tenant, (Guid)message!.GetType().GetProperty("TenantId")!.GetValue(message)!);
        Assert.Equal(["save", "publish:IEmployeeCreated", "save"], probe.Log.Take(3));
    }

    [Fact]
    public void The_consumer_outbox_runs_at_read_committed()
    {
        var services = new ServiceCollection();
        services.AddDbContext<HRCoreDbContext>(o => o.UseInMemoryDatabase("msg040-isolation"));
        services.AddMassTransit(x => x.AddPostgresOutbox<HRCoreDbContext>());
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<EntityFrameworkOutboxOptions<HRCoreDbContext>>>().Value;

        Assert.Equal(IsolationLevel.ReadCommitted, options.IsolationLevel);
    }

    [Theory]
    [InlineData("", false, false)]
    [InlineData("/careers", false, true)]
    [InlineData("/ui/hr-core", false, true)]
    [InlineData("", true, true)]
    public void Metrics_are_refused_for_a_request_that_came_through_the_gateway(string pathBase, bool forwarded, bool proxied)
    {
        var http = new DefaultHttpContext();
        http.Request.PathBase = pathBase;
        if (forwarded)
            http.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        Assert.Equal(proxied, OpenTelemetryExtensions.IsProxied(http.Request));
    }
}
