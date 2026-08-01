using System.Security.Claims;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;

namespace HRManagement.UnitTests;

public class SensitiveDataAuditTests
{
    [Fact]
    public void Log_with_httpContext_extracts_user_name()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "admin-user")], "Test"));

        var ex = Record.Exception(() =>
            SensitiveDataAudit.Log(ctx, "Health", "View", "MedicalRecord:42"));

        Assert.Null(ex);
    }

    [Fact]
    public void Log_without_httpContext_does_not_throw()
    {
        var ex = Record.Exception(() =>
            SensitiveDataAudit.Log(null, "Compliance", "Export", "Case:7"));

        Assert.Null(ex);
    }

    [Fact]
    public void Log_with_anonymous_context_does_not_throw()
    {
        var ctx = new DefaultHttpContext();
        var ex = Record.Exception(() =>
            SensitiveDataAudit.Log(ctx, "Health", "Update", "Record:1"));

        Assert.Null(ex);
    }

    [Fact]
    public void Log_with_preferred_username_claim()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("preferred_username", "keycloak-user")], "Test"));

        var ex = Record.Exception(() =>
            SensitiveDataAudit.Log(ctx, "Health", "Read", "Patient:99"));

        Assert.Null(ex);
    }
}
