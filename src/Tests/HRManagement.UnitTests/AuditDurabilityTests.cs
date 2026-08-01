using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.Gateway.Data;
using HRManagement.Gateway.Middleware;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

public class AuditDurabilityTests : IDisposable
{
    private readonly string _overflowDir;

    public AuditDurabilityTests()
    {
        _overflowDir = Path.Combine(Path.GetTempPath(), "hrm-audit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_overflowDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_overflowDir))
                Directory.Delete(_overflowDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static GatewayAuditEntry Sample(string path = "/api/test", int status = 200) => new()
    {
        OccurredAtUtc = DateTimeOffset.UtcNow,
        CorrelationId = Guid.NewGuid().ToString("N"),
        Method = "GET",
        Path = path,
        QueryString = "",
        StatusCode = status,
        ElapsedMs = 1,
        Decision = "allowed"
    };

    private GatewayAuditQueue CreateQueue(
        IServiceProvider services,
        int capacity = 8192,
        int maxRetries = 3,
        int[]? delays = null)
    {
        var opts = Options.Create(new AuditDurabilityOptions
        {
            ChannelCapacity = capacity,
            OverflowPath = _overflowDir,
            MaxRetries = maxRetries,
            RetryDelaysMs = delays ?? [1, 1, 1],
            OverflowSweepSeconds = 30
        });
        return new GatewayAuditQueue(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<GatewayAuditQueue>.Instance,
            opts);
    }

    private static ServiceProvider CreateDbProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<GatewayAuditDbContext>(o =>
            o.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>().Database.EnsureCreated();
        return provider;
    }

    [Fact]
    public async Task ChannelFull_SpillsToOverflowFile_NotDropped()
    {
        await using var root = CreateDbProvider(Guid.NewGuid().ToString("N"));
        var queue = CreateQueue(root, capacity: 2);

        Assert.True(queue.TryEnqueue(Sample("/api/a")));
        Assert.True(queue.TryEnqueue(Sample("/api/b")));
        // Third write should spill because DropWrite + capacity 2
        Assert.True(queue.TryEnqueue(Sample("/api/c")));

        Assert.True(queue.OverflowFileCount >= 1,
            "overflow file must be written when channel is full");
        var files = Directory.GetFiles(_overflowDir, "overflow-*.jsonl");
        Assert.NotEmpty(files);
        var lines = await System.IO.File.ReadAllLinesAsync(files[0]);
        Assert.Contains(lines, l => l.Contains("/api/c", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DbFailure_RetriesThenWritesDlq()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServiceScopeFactory, ThrowingScopeFactory>();
        await using var root = services.BuildServiceProvider();
        var queue = CreateQueue(root, maxRetries: 3, delays: [1, 1, 1]);

        var batch = new List<GatewayAuditEntry> { Sample("/api/fail") };
        await queue.PersistWithRetryAsync(batch, CancellationToken.None);

        Assert.True(queue.DlqFileCount >= 1);
        var dlq = Directory.GetFiles(_overflowDir, "*_dlq.jsonl");
        Assert.NotEmpty(dlq);
        var content = await System.IO.File.ReadAllTextAsync(dlq[0]);
        Assert.Contains("/api/fail", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GracefulShutdown_DrainsInChannelEvents()
    {
        await using var root = CreateDbProvider(Guid.NewGuid().ToString("N"));
        var queue = CreateQueue(root);
        await queue.StartAsync(CancellationToken.None);

        for (var i = 0; i < 5; i++)
            Assert.True(queue.TryEnqueue(Sample($"/api/drain/{i}")));

        // Give the worker a moment, then stop (drains remainder).
        await Task.Delay(50);
        await queue.StopAsync(CancellationToken.None);

        using var scope = root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
        var count = await db.AuditEntries.CountAsync();
        Assert.True(count >= 5, $"expected drained events persisted, got {count}");
    }

    [Fact]
    public async Task SequenceNumbers_AreMonotonic_InDatabase()
    {
        await using var root = CreateDbProvider(Guid.NewGuid().ToString("N"));
        var queue = CreateQueue(root);
        await queue.StartAsync(CancellationToken.None);

        for (var i = 0; i < 20; i++)
            Assert.True(queue.TryEnqueue(Sample($"/api/seq/{i}")));

        await Task.Delay(200);
        await queue.StopAsync(CancellationToken.None);

        using var scope = root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
        var sequences = await db.AuditEntries
            .OrderBy(x => x.SequenceNumber)
            .Select(x => x.SequenceNumber)
            .ToListAsync();

        Assert.Equal(20, sequences.Count);
        Assert.Equal(sequences.OrderBy(x => x), sequences);
        Assert.Equal(sequences.Distinct().Count(), sequences.Count);
        Assert.Equal(1, sequences.Min());
        Assert.Equal(20, sequences.Max());
    }

    [Fact]
    public void SensitiveDataAudit_PersistsViaRegisteredSink()
    {
        var sink = new CapturingDurableSink();
        var services = new ServiceCollection();
        services.AddSingleton<IDurableAuditSink>(sink);
        var provider = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = provider };
        SensitiveDataAudit.Log(ctx, "Health", "View", "MedicalRecord:42");

        Assert.Single(sink.Events);
        Assert.Equal("Health", sink.Events[0].Domain);
        Assert.Equal("View", sink.Events[0].Action);
        Assert.Equal("MedicalRecord:42", sink.Events[0].ResourceSummary);
    }

    [Fact]
    public void SensitiveDataAudit_WithoutSink_WritesFileFallback()
    {
        var dir = Path.Combine(_overflowDir, "sensitive-fallback");
        Environment.SetEnvironmentVariable("HR_AUDIT_OVERFLOW_PATH", dir);
        try
        {
            SensitiveDataAudit.Log(null, "Compliance", "Export", "Case:7");
            Assert.True(Directory.Exists(dir));
            var files = Directory.GetFiles(dir, "sensitive-*.jsonl");
            Assert.NotEmpty(files);
            var text = System.IO.File.ReadAllText(files[0]);
            Assert.Contains("Case:7", text, StringComparison.Ordinal);
            Assert.Contains("sensitive_access", text, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HR_AUDIT_OVERFLOW_PATH", null);
        }
    }

    [Fact]
    public async Task PiiFields_AreRedactedInEntityAuditLog()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<HRCoreDbContext>((sp, o) =>
            o.UseSqlite(connection)
             .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

        await using var root = services.BuildServiceProvider();
        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
        await db.Database.EnsureCreatedAsync();

        var emp = new Employee
        {
            NationalId = "12345678901",
            FirstName = "أحمد",
            LastName = "اختبار",
            FullName = "أحمد اختبار",
            BirthDate = new DateTime(1990, 1, 1),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        emp.NationalId = "99999999999";
        await db.SaveChangesAsync();

        var audit = await db.AuditLogs
            .Where(a => a.EntityId == emp.Id && a.Action == "Updated")
            .OrderByDescending(a => a.CreatedAt)
            .FirstAsync();

        Assert.NotNull(audit.ChangedFields);
        Assert.Contains("***", audit.ChangedFields);
        Assert.DoesNotContain("99999999999", audit.ChangedFields);
        Assert.DoesNotContain("12345678901", audit.ChangedFields);
    }

    [Fact]
    public async Task GatewayAuditEntries_RejectUpdateAndDelete()
    {
        await using var root = CreateDbProvider(Guid.NewGuid().ToString("N"));
        using var scope = root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();

        var entry = Sample("/api/immutable");
        entry.SequenceNumber = 1;
        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync();

        entry.Decision = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        var stored = await db.AuditEntries.FirstAsync();
        db.AuditEntries.Remove(stored);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task EntityAuditLog_RejectsUpdateAndDelete()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<HRCoreDbContext>((sp, o) =>
            o.UseSqlite(connection)
             .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

        await using var root = services.BuildServiceProvider();
        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
        await db.Database.EnsureCreatedAsync();

        db.AuditLogs.Add(new EntityAuditLog
        {
            EntityType = "Test",
            EntityId = Guid.NewGuid(),
            Action = "Created"
        });
        await db.SaveChangesAsync();

        var row = await db.AuditLogs.FirstAsync();
        row.Action = "Tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Middleware_Classifies_4xx_And_5xx()
    {
        await AssertDecision(StatusCodes.Status404NotFound, "client_error");
        await AssertDecision(StatusCodes.Status500InternalServerError, "server_error");
        await AssertDecision(StatusCodes.Status401Unauthorized, "challenge");
        await AssertDecision(StatusCodes.Status403Forbidden, "forbidden");
        await AssertDecision(StatusCodes.Status200OK, "allowed");
    }

    private static async Task AssertDecision(int status, string expectedDecision)
    {
        var queue = new CapturingAuditQueue();
        var middleware = new GatewayAuditMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = status;
            await Task.CompletedTask;
        });
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/api/demo";
        await middleware.InvokeAsync(ctx, queue, NoopSecurityAudit.Instance, NullLogger<GatewayAuditMiddleware>.Instance);
        Assert.Equal(expectedDecision, queue.Last!.Decision);
    }

    private sealed class CapturingAuditQueue : IGatewayAuditQueue
    {
        public GatewayAuditEntry? Last { get; private set; }
        public int OverflowFileCount => 0;
        public int DlqFileCount => 0;
        public long PeekNextSequenceNumber() => 1;
        public bool TryEnqueue(GatewayAuditEntry entry)
        {
            Last = entry;
            return true;
        }
    }

    private sealed class NoopSecurityAudit : ISecurityAuditPublisher
    {
        public static readonly NoopSecurityAudit Instance = new();
        public Task PublishAsync(SecurityAuditEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public void RecordLookupHit(string? actorSub, string correlationId) { }
    }

    private sealed class CapturingDurableSink : IDurableAuditSink
    {
        public List<DurableAuditEvent> Events { get; } = [];
        public bool TryPersist(DurableAuditEvent auditEvent)
        {
            Events.Add(auditEvent);
            return true;
        }
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new ThrowingScope();

        private sealed class ThrowingScope : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new ThrowingProvider();
            public void Dispose() { }
        }

        private sealed class ThrowingProvider : IServiceProvider
        {
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(GatewayAuditDbContext))
                    throw new DbUpdateException("simulated audit db failure");
                return null;
            }
        }
    }
}
