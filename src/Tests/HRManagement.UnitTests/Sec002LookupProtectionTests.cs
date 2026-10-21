using System.Reflection;
using System.Text.RegularExpressions;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.UnitTests;

/// <summary>SEC-002: Lookup authorization, narrow DTO, search/rate limits.</summary>
public class Sec002LookupProtectionTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "src", "Gateway", "HRManagement.Gateway", "Program.cs")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    private static Type LookupItemDtoType =>
        typeof(LookupApiController).GetNestedType(nameof(LookupApiController.LookupItemDto), BindingFlags.Public)
        ?? throw new InvalidOperationException("LookupItemDto nested type missing");

    [Fact]
    public void LookupApiController_requires_Authorize_with_HrCoreLookup_policy()
    {
        var attr = typeof(LookupApiController).GetCustomAttribute<AuthorizeAttribute>(inherit: true);
        Assert.NotNull(attr);
        Assert.Equal(HrManagementPolicies.HrCoreLookup, attr!.Policy);
    }

    [Fact]
    public void LookupItemDto_does_not_expose_NationalId()
    {
        var names = LookupItemDtoType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Concat(LookupItemDtoType.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name))
            .ToList();

        Assert.DoesNotContain(names, n => n.Contains("NationalId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("National", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LookupItemDto_only_contains_allowed_fields()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "Id", "Label", "Sublabel" };
        var props = LookupItemDtoType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(allowed, props);
    }

    [Fact]
    public void MinSearchLength_is_2()
    {
        var field = typeof(LookupApiController).GetField(
            "MinSearchLength",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.True(field!.IsLiteral, "MinSearchLength should be a const");
        Assert.Equal(2, field.GetRawConstantValue());
    }

    [Fact]
    public void LookupMaxPageSize_is_50()
    {
        Assert.Equal(50, SearchFilterBase.LookupMaxPageSize);
    }

    [Fact]
    public void Gateway_rate_limiter_is_registered()
    {
        var path = Path.Combine(ResolveRepoRoot(), "src", "Gateway", "HRManagement.Gateway", "Program.cs");
        var source = System.IO.File.ReadAllText(path);
        Assert.Contains("AddRateLimiter", source, StringComparison.Ordinal);
        Assert.Contains("UseRateLimiter", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LookupApiController_has_no_AllowAnonymous_on_class()
    {
        var attrs = typeof(LookupApiController).GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false);
        Assert.Empty(attrs);

        // Source guard: class-level attribute must not appear above the type.
        var controllerPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "HRCore", "HR.Core.Web", "Controllers", "LookupApiController.cs");
        var source = System.IO.File.ReadAllText(controllerPath);
        Assert.False(
            Regex.IsMatch(source, @"\[AllowAnonymous[^\]]*\]\s*(?:\[[^\]]+\]\s*)*public\s+class\s+LookupApiController"),
            "LookupApiController class must not have [AllowAnonymous]");
    }

    [Fact]
    public void LookupApiController_batch_endpoints_require_HrCoreLookup_and_limit_batch_size()
    {
        var batchMethods = new[]
        {
            nameof(LookupApiController.GetEmployeesByIds),
            nameof(LookupApiController.GetOrgUnitsByIds)
        };

        foreach (var methodName in batchMethods)
        {
            var method = typeof(LookupApiController).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(method);

            var post = method!.GetCustomAttribute<HttpPostAttribute>();
            Assert.NotNull(post);
            Assert.EndsWith("by-ids", post!.Template, StringComparison.Ordinal);

            var authorize = typeof(LookupApiController).GetCustomAttribute<AuthorizeAttribute>(inherit: true);
            Assert.NotNull(authorize);
            Assert.Equal(HrManagementPolicies.HrCoreLookup, authorize!.Policy);
        }

        var controllerPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "HRCore", "HR.Core.Web", "Controllers", "LookupApiController.cs");
        var source = System.IO.File.ReadAllText(controllerPath);
        Assert.Contains("SearchFilterBase.LookupMaxPageSize", source, StringComparison.Ordinal);
        Assert.Contains("BadRequest", source, StringComparison.Ordinal);
    }
}
