using System.Net;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// File-reference outbox across tenants: the dispatcher runs in a background scope with no tenant
/// (EffectiveTenantId = Guid.Empty), so it must read every tenant's due rows through the sanctioned bypass and send
/// each row with its own TenantId as X-Tenant-ID. Before the fix it read under the tenant filter (no rows) and sent no
/// tenant header.
/// </summary>
public class FileReferenceOutboxTenantDeliveryTests
{
    private static readonly Guid TenantA = Guid.Parse("f1e00000-0000-4000-8000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("f1e00000-0000-4000-8000-00000000000b");

    [Fact]
    public async Task Background_dispatch_delivers_every_tenants_rows_with_each_rows_tenant()
    {
        await using var connection = Open();
        var fileA = Guid.NewGuid();
        var fileB = Guid.NewGuid();
        await EnqueueAsync(connection, TenantA, o => o.EnqueueRegister(fileA, "hr-core:CandidateDocument:a:FileId"));
        await EnqueueAsync(connection, TenantB, o =>
        {
            o.EnqueueRegister(fileB, "hr-core:OfferLetter:b:AcceptanceEvidence");
            o.EnqueueRelease(fileB, "hr-core:CandidateDocument:b:FileId");
        });

        var delivery = new RecordingDelivery();
        await using (var background = BackgroundContext(connection))
        {
            Assert.Equal(Guid.Empty, background.EffectiveTenantId);
            Assert.Equal(0, await background.FileReferenceOutbox.CountAsync());

            var processed = await FileReferenceOutboxDispatcherJob<HRCoreDbContext>.ProcessBatchAsync(
                background, delivery, CancellationToken.None);
            Assert.Equal(3, processed);
        }

        Assert.Equal(
            new List<(Guid TenantId, string Operation, Guid FileId)>
            {
                (TenantA, FileReferenceOutboxOps.Register, fileA),
                (TenantB, FileReferenceOutboxOps.Register, fileB),
                (TenantB, FileReferenceOutboxOps.Release, fileB)
            },
            delivery.Calls.OrderBy(c => c.TenantId).ThenBy(c => c.Operation, StringComparer.Ordinal).ToList());

        foreach (var tenant in new[] { TenantA, TenantB })
        {
            await using var check = TenantScoped(connection, tenant);
            var rows = await check.FileReferenceOutbox.AsNoTracking().ToListAsync();
            Assert.NotEmpty(rows);
            Assert.All(rows, r =>
            {
                Assert.Equal(FileReferenceOutboxStatus.Succeeded, r.Status);
                Assert.Equal(tenant, r.TenantId);
                Assert.NotNull(r.ProcessedAt);
            });
        }
    }

    [Fact]
    public async Task A_failed_delivery_keeps_its_tenant_and_is_retried_later()
    {
        await using var connection = Open();
        var file = Guid.NewGuid();
        await EnqueueAsync(connection, TenantB, o => o.EnqueueRegister(file, "hr-core:CandidateDocument:c:FileId"));

        await using (var background = BackgroundContext(connection))
        {
            var processed = await FileReferenceOutboxDispatcherJob<HRCoreDbContext>.ProcessBatchAsync(
                background, new RecordingDelivery { Fail = true }, CancellationToken.None);
            Assert.Equal(0, processed);
        }

        await using var check = TenantScoped(connection, TenantB);
        var row = await check.FileReferenceOutbox.AsNoTracking().SingleAsync();
        Assert.Equal(FileReferenceOutboxStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempt);
        Assert.Equal(TenantB, row.TenantId);
        Assert.True(row.NextAttemptAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task An_untenanted_row_is_dead_and_never_sent()
    {
        await using var connection = Open();
        await using (var raw = BackgroundContext(connection, stampTenant: false))
        {
            raw.FileReferenceOutbox.Add(new FileReferenceOutboxMessage
            {
                FileId = Guid.NewGuid(),
                HolderKey = "hr-core:Legacy:x:FileId",
                Operation = FileReferenceOutboxOps.Register,
                NextAttemptAt = DateTime.UtcNow.AddMinutes(-1),
                TenantId = Guid.Empty
            });
            await raw.SaveChangesAsync();
        }

        var delivery = new RecordingDelivery();
        await using (var background = BackgroundContext(connection))
        {
            Assert.Equal(0, await FileReferenceOutboxDispatcherJob<HRCoreDbContext>.ProcessBatchAsync(
                background, delivery, CancellationToken.None));
        }

        Assert.Empty(delivery.Calls);
        await using var check = BackgroundContext(connection);
        var row = await check.FileReferenceOutbox
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(FileReferenceOutboxStatus.Dead, row.Status);
        Assert.Equal(FileReferenceOutboxDispatcherJob<HRCoreDbContext>.MissingTenantError, row.LastError);
    }

    [Fact]
    public async Task Http_delivery_sends_the_rows_tenant_even_when_another_tenant_is_ambient()
    {
        var handler = new RecordingHandler();
        await using var provider = HttpProvider(handler);
        var client = provider.GetRequiredService<HttpFileReferenceClient>();
        var fileId = Guid.Parse("6c6d8e2f-3c21-45d6-90ee-756bf8b1a732");

        var previous = TenantIdFlow.Get();
        TenantIdFlow.Set(TenantA);
        try
        {
            await client.RegisterReferenceForTenantAsync(TenantB, fileId, "hr-core:CandidateDocument:d:FileId");
            await client.ReleaseReferenceForTenantAsync(TenantB, fileId, "hr-core:CandidateDocument:d:FileId");
            // The request-path methods keep forwarding the ambient tenant.
            await client.RegisterReferenceAsync(fileId, "hr-core:CandidateDocument:d:FileId");
        }
        finally
        {
            TenantIdFlow.Set(previous);
        }

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
        Assert.Equal($"http://file-storage/api/files/{fileId:D}/references", handler.Requests[0].Uri);
        Assert.Equal(TenantB.ToString("D"), Assert.Single(handler.Requests[0].Tenant));
        Assert.Equal(TenantB.ToString("D"), Assert.Single(handler.Requests[1].Tenant));
        Assert.Equal(TenantA.ToString("D"), Assert.Single(handler.Requests[2].Tenant));
        Assert.Contains("hr-core:CandidateDocument:d:FileId", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_delivery_refuses_an_empty_tenant()
    {
        await using var provider = HttpProvider(new RecordingHandler());
        var client = provider.GetRequiredService<HttpFileReferenceClient>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.RegisterReferenceForTenantAsync(Guid.Empty, Guid.NewGuid(), "hr-core:X:y:FileId"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ReleaseReferenceForTenantAsync(Guid.Empty, Guid.NewGuid(), "hr-core:X:y:FileId"));
    }

    [Fact]
    public void Dispatcher_reads_through_the_sanctioned_background_bypass()
    {
        var source = System.IO.File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Shared", "HRManagement.ServiceDefaults", "FileReferenceOutboxDispatcher.cs"));
        Assert.Contains(
            ".IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.BackgroundQueueProcessing)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("IgnoreQueryFilters", source, StringComparison.Ordinal);
    }

    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = BackgroundContext(connection);
        db.Database.EnsureCreated();
        return connection;
    }

    /// <summary>The dispatcher's scope: no tenant, production stamp interceptor.</summary>
    private static HRCoreDbContext BackgroundContext(SqliteConnection connection, bool stampTenant = true)
    {
        var builder = new DbContextOptionsBuilder<HRCoreDbContext>().UseSqlite(connection);
        var noTenant = Mock.Of<ITenantContext>();
        if (stampTenant)
            builder.AddInterceptors(new TenantIdStampInterceptor(noTenant));
        return new HRCoreDbContext(builder.Options, noTenant);
    }

    private static HRCoreDbContext TenantScoped(SqliteConnection connection, Guid tenantId)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        return new HRCoreDbContext(
            new DbContextOptionsBuilder<HRCoreDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new TenantIdStampInterceptor(tenant.Object))
                .Options,
            tenant.Object);
    }

    /// <summary>Writes outbox rows as a request in <paramref name="tenantId"/> would (EfFileReferenceOutbox + stamp).</summary>
    private static async Task EnqueueAsync(SqliteConnection connection, Guid tenantId, Action<EfFileReferenceOutbox> enqueue)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(tenantId);
        await using var db = TenantScoped(connection, tenantId);
        enqueue(new EfFileReferenceOutbox(db, tenant.Object));
        await db.SaveChangesAsync();
    }

    private static ServiceProvider HttpProvider(RecordingHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddTransient<ForwardTenantHandler>();
        services.AddHttpClient("FileStorage", c => c.BaseAddress = new Uri("http://file-storage/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddHttpMessageHandler<ForwardTenantHandler>();
        services.AddScoped<HttpFileReferenceClient>();
        return services.BuildServiceProvider();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "docs", "FILE_SERVICE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    private sealed class RecordingDelivery : ITenantFileReferenceDelivery
    {
        public bool Fail { get; init; }
        public List<(Guid TenantId, string Operation, Guid FileId)> Calls { get; } = [];

        public Task RegisterReferenceForTenantAsync(Guid tenantId, Guid fileId, string holderKey, CancellationToken cancellationToken = default) =>
            Record(tenantId, FileReferenceOutboxOps.Register, fileId);

        public Task ReleaseReferenceForTenantAsync(Guid tenantId, Guid fileId, string holderKey, CancellationToken cancellationToken = default) =>
            Record(tenantId, FileReferenceOutboxOps.Release, fileId);

        private Task Record(Guid tenantId, string operation, Guid fileId)
        {
            if (Fail)
                throw new HttpRequestException("File.Storage unavailable");
            Calls.Add((tenantId, operation, fileId));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string[] Tenant, string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tenant = request.Headers.TryGetValues(TenantIdMiddleware.HeaderName, out var values) ? values.ToArray() : [];
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsoluteUri, tenant, body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"referenceCount\":1}") };
        }
    }
}
