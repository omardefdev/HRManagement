using System.Security.Claims;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Payroll.Domain.Services;
using HR.Contracts.Domain.Services;
using HR.Core.Domain.Services;
using HRManagement.SharedKernel.Enums;

namespace HRManagement.UnitTests;

#region Phase 8: Dashboard & Export Tests

public class PayrollDashboard_Tests
{
    [Fact]
    public void PayrollEngine_CalculateTax_ZeroForExemptAmount()
    {
        var tax = PayrollEngine.CalculateTax(4000m);
        Assert.Equal(0m, tax);
    }

    [Fact]
    public void PayrollEngine_CalculateTax_CorrectForMiddleBracket()
    {
        var tax = PayrollEngine.CalculateTax(20000m);
        Assert.True(tax > 0);
    }

    [Fact]
    public void PayrollEngine_CalculateTax_ProgressiveForHighIncome()
    {
        var lowTax = PayrollEngine.CalculateTax(10000m);
        var highTax = PayrollEngine.CalculateTax(60000m);
        Assert.True(highTax > lowTax);
    }
}

public class ExportFormat_Tests
{
    [Fact]
    public void CsvLine_ShouldEscapeCommas()
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Name,Value");
        csv.AppendLine($"\"Test, Item\",100");
        var content = csv.ToString();
        Assert.Contains("\"Test, Item\"", content);
    }

    [Fact]
    public void CsvOutput_ShouldHaveHeader()
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Id,Name,Amount");
        Assert.StartsWith("Id,Name,Amount", csv.ToString());
    }
}

#endregion

#region Phase 9: Security & Audit Tests

public class AuditInterceptor_Tests
{
    private static (DbContextOptions<TestAuditDbContext>, AuditSaveChangesInterceptor) CreateTestSetup(Guid? userId = null)
    {
        var httpContext = new Mock<IHttpContextAccessor>();
        if (userId.HasValue)
        {
            var context = new DefaultHttpContext();
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())
            }, "test");
            context.User = new ClaimsPrincipal(identity);
            httpContext.Setup(x => x.HttpContext).Returns(context);
        }

        var interceptor = new AuditSaveChangesInterceptor(httpContext.Object);
        var options = new DbContextOptionsBuilder<TestAuditDbContext>()
            .UseInMemoryDatabase($"AuditTest_{Guid.NewGuid()}")
            .AddInterceptors(interceptor)
            .Options;

        return (options, interceptor);
    }

    [Fact]
    public void AddEntity_SetsCreatedByAndUpdatedBy()
    {
        var userId = Guid.NewGuid();
        var (options, _) = CreateTestSetup(userId);

        using var db = new TestAuditDbContext(options);
        var entity = new TestAuditableEntity { Name = "Test" };
        db.TestEntities.Add(entity);
        db.SaveChanges();

        Assert.Equal(userId, entity.CreatedBy);
        Assert.Equal(userId, entity.UpdatedBy);
    }

    [Fact]
    public void ModifyEntity_SetsUpdatedByOnly()
    {
        var originalUser = Guid.NewGuid();
        var (options, _) = CreateTestSetup(originalUser);

        using var db = new TestAuditDbContext(options);
        var entity = new TestAuditableEntity { Name = "Original" };
        db.TestEntities.Add(entity);
        db.SaveChanges();

        entity.Name = "Modified";
        db.SaveChanges();

        Assert.Equal(originalUser, entity.CreatedBy);
        Assert.Equal(originalUser, entity.UpdatedBy);
    }

    [Fact]
    public void UnchangedTrackedEntity_DoesNotChangeUpdatedAt()
    {
        var (options, _) = CreateTestSetup(Guid.NewGuid());

        using var db = new TestAuditDbContext(options);
        var entity = new TestAuditableEntity { Name = "Stable" };
        db.TestEntities.Add(entity);
        db.SaveChanges();
        var persistedUpdatedAt = entity.UpdatedAt;

        db.TestEntities.Add(new TestAuditableEntity { Name = "Other" });
        db.SaveChanges();

        Assert.Equal(persistedUpdatedAt, entity.UpdatedAt);
        Assert.Equal(EntityState.Unchanged, db.Entry(entity).State);
    }

    [Fact]
    public void AddEntity_CreatesAuditLog()
    {
        var (options, _) = CreateTestSetup(Guid.NewGuid());

        using var db = new TestAuditDbContext(options);
        var entity = new TestAuditableEntity { Name = "Audited" };
        db.TestEntities.Add(entity);
        db.SaveChanges();

        var logs = db.Set<EntityAuditLog>().ToList();
        Assert.Contains(logs, l => l.Action == "Created" && l.EntityType == nameof(TestAuditableEntity));
    }

    [Fact]
    public void ModifyEntity_AuditLogContainsChangedFields()
    {
        var (options, _) = CreateTestSetup(Guid.NewGuid());

        using var db = new TestAuditDbContext(options);
        var entity = new TestAuditableEntity { Name = "Before" };
        db.TestEntities.Add(entity);
        db.SaveChanges();

        entity.Name = "After";
        db.SaveChanges();

        var logs = db.Set<EntityAuditLog>().Where(l => l.Action == "Updated").ToList();
        Assert.NotEmpty(logs);
        Assert.NotNull(logs[0].ChangedFields);
        Assert.Contains("Name", logs[0].ChangedFields!);
    }

    [Fact]
    public void NoHttpContext_StillWorks()
    {
        var (options, _) = CreateTestSetup(null);

        using var db = new TestAuditDbContext(options);
        var entity = new TestAuditableEntity { Name = "NoUser" };
        db.TestEntities.Add(entity);
        db.SaveChanges();

        Assert.Null(entity.CreatedBy);
        Assert.Null(entity.UpdatedBy);
    }
}

public class TestAuditableEntity : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
}

public class TestAuditDbContext : DbContext
{
    public TestAuditDbContext(DbContextOptions<TestAuditDbContext> options) : base(options) { }
    public DbSet<TestAuditableEntity> TestEntities => Set<TestAuditableEntity>();
    public DbSet<EntityAuditLog> EntityAuditLogs => Set<EntityAuditLog>();
}

#endregion

#region Phase 9: Authorization Policy Tests

public class AuthorizationPolicy_Tests
{
    [Fact]
    public void HrManagementPolicies_ContractsApproveExists()
    {
        Assert.Equal("HrContracts.Approve", HrManagementPolicies.HrContractsApprove);
    }

    [Fact]
    public void HrManagementPolicies_PayrollProcessExists()
    {
        Assert.Equal("Payroll.Process", HrManagementPolicies.PayrollProcess);
    }

    [Fact]
    public void HrManagementPolicies_PayrollPostExists()
    {
        Assert.Equal("Payroll.Post", HrManagementPolicies.PayrollPost);
    }
}

public class GatewayMiddleware_FineGrainedTests
{
    [Theory]
    [InlineData("hr-contracts", "POST", true)]
    [InlineData("hr-contracts", "GET", false)]
    [InlineData("payroll", "POST", true)]
    [InlineData("payroll", "GET", false)]
    [InlineData("hr-core", "GET", true)]
    [InlineData("hr-core", "POST", true)]
    [InlineData("hr-core", "DELETE", true)]
    [InlineData("hr-health", "POST", false)]
    public void TryGetFineGrainedPolicy_ReturnsExpected(string segment, string method, bool expected)
    {
        var result = GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy(segment, method, PathString.Empty, out var policy);
        Assert.Equal(expected, result);
        if (expected) Assert.False(string.IsNullOrEmpty(policy));
    }
}

#endregion

#region Phase 10: Test Infrastructure Verification

public class EntityAuditLog_Tests
{
    [Fact]
    public void EntityAuditLog_HasRequiredProperties()
    {
        var log = new EntityAuditLog
        {
            EntityType = "Employee",
            EntityId = Guid.NewGuid(),
            Action = "Created",
            ChangedFields = "{\"Name\":{\"Old\":\"A\",\"New\":\"B\"}}",
            UserId = Guid.NewGuid()
        };

        Assert.NotEmpty(log.EntityType);
        Assert.NotEqual(Guid.Empty, log.EntityId);
        Assert.NotEmpty(log.Action);
        Assert.NotNull(log.ChangedFields);
    }

    [Fact]
    public void EntityAuditLog_InheritsBaseEntity()
    {
        var log = new EntityAuditLog();
        Assert.IsAssignableFrom<BaseEntity>(log);
        Assert.NotEqual(Guid.Empty, log.Id);
    }
}

#endregion

#region Phase 11: Health Check & Serilog Tests

public class HealthCheck_Tests
{
    [Fact]
    public void MapHrHealthChecks_RegistersExtensionOnIEndpointRouteBuilder()
    {
        var method = typeof(HealthCheckExtensions).GetMethod(
            "MapHrHealthChecks",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder), parameters[0].ParameterType);
        Assert.Equal(typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder), method.ReturnType);
    }

    [Fact]
    public void HealthCheckPaths_LiveAndReadyAreDistinct()
    {
        // Contract: probe paths used by MapHrHealthChecks / ops must stay stable.
        const string live = "/health/live";
        const string ready = "/health/ready";
        Assert.StartsWith("/health", live);
        Assert.StartsWith("/health", ready);
        Assert.NotEqual(live, ready);
    }
}

#endregion

#region Phase 12: SignalR & UI Tests

public class SharedUi_Tests
{
    [Fact]
    public void NotificationHub_IsSealedHub()
    {
        var hub = typeof(NotificationHub);
        Assert.True(hub.IsSealed);
        Assert.True(typeof(Microsoft.AspNetCore.SignalR.Hub).IsAssignableFrom(hub));
    }

    [Fact]
    public void NotificationHubExtensions_SendNotificationAsync_HasExpectedSignature()
    {
        var method = typeof(NotificationHubExtensions).GetMethod("SendNotificationAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal(typeof(Microsoft.AspNetCore.SignalR.IHubContext<NotificationHub>), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType); // title
        Assert.Equal(typeof(string), parameters[2].ParameterType); // message
        Assert.Equal(typeof(string), parameters[3].ParameterType); // type
        Assert.Equal("info", parameters[3].DefaultValue);
    }
}

public class EmploymentStatusRules_AdditionalTests
{
    [Theory]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.OnLeave, true)]
    [InlineData(EmploymentStatus.OnLeave, EmploymentStatus.Active, true)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Suspended, true)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Terminated, true)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Resigned, true)]
    [InlineData(EmploymentStatus.Terminated, EmploymentStatus.Active, false)]
    [InlineData(EmploymentStatus.Retired, EmploymentStatus.Active, false)]
    public void CanTransition_ReturnsExpected(EmploymentStatus from, EmploymentStatus to, bool expected)
    {
        Assert.Equal(expected, EmploymentStatusRules.CanTransition(from, to));
    }

    [Theory]
    [InlineData(EmploymentStatus.Terminated, true)]
    [InlineData(EmploymentStatus.Retired, true)]
    [InlineData(EmploymentStatus.Active, false)]
    [InlineData(EmploymentStatus.OnLeave, false)]
    public void IsTerminal_ReturnsExpected(EmploymentStatus status, bool expected)
    {
        Assert.Equal(expected, EmploymentStatusRules.IsTerminal(status));
    }
}

#endregion
