using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace HRManagement.UnitTests;

public class HrRequestClassificationTests
{
    [Fact]
    public void WantsJsonResponse_true_for_api_path()
    {
        var ctx = new DefaultHttpContext { Request = { Path = "/api/assets" } };
        Assert.True(HrRequestClassification.WantsJsonResponse(ctx));
    }

    [Fact]
    public void WantsJsonResponse_true_for_xhr()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        Assert.True(HrRequestClassification.WantsJsonResponse(ctx));
    }

    [Fact]
    public void WantsJsonResponse_false_for_form_post()
    {
        var ctx = new DefaultHttpContext
        {
            Request =
            {
                Path = "/Assets/CreateLegacy",
                ContentType = "application/x-www-form-urlencoded",
                Method = "POST"
            }
        };
        ctx.Request.Headers.Accept = "text/html";
        Assert.False(HrRequestClassification.WantsJsonResponse(ctx));
    }
}

public class UniqueConstraintMessageRegistryTests
{
    [Fact]
    public void Resolve_tag_number_constraint()
    {
        var resolution = UniqueConstraintMessageRegistry.Resolve(constraintName: "IX_Assets_TenantId_TagNumber");
        Assert.Equal("TagNumber", resolution.ModelStateKey);
        Assert.Contains("عهدة", resolution.Message);
    }

    [Fact]
    public void Resolve_unknown_constraint_uses_default()
    {
        var resolution = UniqueConstraintMessageRegistry.Resolve(constraintName: "IX_Unknown");
        Assert.Equal(ConcurrencyConflictMessages.UniqueViolation, resolution.Message);
        Assert.Null(resolution.ModelStateKey);
    }

    [Fact]
    public void TryGetUniqueConstraintName_parses_postgres_message()
    {
        var ex = new DbUpdateException(
            "fail",
            new Exception("23505: duplicate key value violates unique constraint \"IX_Assets_TenantId_TagNumber\""));
        Assert.True(ConcurrencyExtensions.TryGetUniqueConstraintName(ex, out var name));
        Assert.Equal("IX_Assets_TenantId_TagNumber", name);
    }
}

public class SafeRedirectRefererTests
{
    [Fact]
    public void TryGetSafeRefererRedirect_rejects_external_host()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("localhost", 5001);
        ctx.Request.Headers.Referer = "https://evil.example/phish";
        Assert.False(SafeRedirect.TryGetSafeRefererRedirect(ctx, out _));
    }

    [Fact]
    public void TryGetSafeRefererRedirect_accepts_same_host_path()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("localhost", 5001);
        ctx.Request.Headers.Referer = "http://localhost:5001/Assets/CreateLegacy";
        Assert.True(SafeRedirect.TryGetSafeRefererRedirect(ctx, out var target));
        Assert.Equal("/Assets/CreateLegacy", target);
    }
}

public class HrSaveChangesTests
{
    private sealed class ThrowOnSaveContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<DummyEntity>().HasKey(x => x.Id);

        public DbSet<DummyEntity> Items => Set<DummyEntity>();

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException(
                "unique",
                new Exception("23505: duplicate key value violates unique constraint \"IX_Assets_TenantId_TagNumber\""));
    }

    private sealed class DummyEntity
    {
        public Guid Id { get; set; }
    }

    [Fact]
    public async Task TryAsync_maps_unique_violation_to_model_state()
    {
        await using var db = new ThrowOnSaveContext();
        db.Items.Add(new DummyEntity { Id = Guid.NewGuid() });
        var modelState = new ModelStateDictionary();

        var outcome = await HrSaveChanges.TryAsync(db, modelState);

        Assert.Equal(SaveChangesOutcome.UniqueViolation, outcome);
        Assert.True(modelState.ContainsKey("TagNumber"));
        Assert.Contains("عهدة", modelState["TagNumber"]!.Errors[0].ErrorMessage);
    }
}

public class ConcurrencyConflictMiddlewareTests
{
    [Fact]
    public async Task Html_form_conflict_redirects_instead_of_json()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddAntiforgery();
        var provider = services.BuildServiceProvider();

        RequestDelegate next = _ => throw new DbUpdateException(
            "unique",
            new Exception("23505: duplicate key value violates unique constraint \"IX_Assets_TenantId_TagNumber\""));

        var middleware = new ConcurrencyConflictMiddleware(next, Mock.Of<ILogger<ConcurrencyConflictMiddleware>>());
        var ctx = new DefaultHttpContext
        {
            RequestServices = provider,
            Request =
            {
                Method = "POST",
                Path = "/Assets/CreateLegacy",
                ContentType = "application/x-www-form-urlencoded",
                Scheme = "http",
                Host = new HostString("localhost", 5001)
            }
        };
        ctx.Request.Headers.Accept = "text/html";
        ctx.Request.Headers.Referer = "http://localhost:5001/Assets/CreateLegacy";

        await middleware.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status302Found, ctx.Response.StatusCode);
        Assert.Equal("/Assets/CreateLegacy", ctx.Response.Headers.Location.ToString());
        Assert.NotEqual("application/json", ctx.Response.ContentType);
    }

    [Fact]
    public async Task Api_conflict_returns_json_409()
    {
        RequestDelegate next = _ => throw new DbUpdateException("unique", new Exception("23505"));
        var middleware = new ConcurrencyConflictMiddleware(next, Mock.Of<ILogger<ConcurrencyConflictMiddleware>>());
        var ctx = new DefaultHttpContext
        {
            Request =
            {
                Method = "POST",
                Path = "/api/assets",
                ContentType = "application/json"
            }
        };
        ctx.Request.Headers.Accept = "application/json";
        ctx.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status409Conflict, ctx.Response.StatusCode);
        Assert.Contains("json", ctx.Response.ContentType ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
