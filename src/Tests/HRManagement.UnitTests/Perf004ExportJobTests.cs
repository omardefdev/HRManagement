using System.Security.Claims;
using System.Text;
using HRManagement.MessageContracts.Commands;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Exports;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.ExportJobs;

namespace HRManagement.UnitTests;

public class Perf004ExportJobTests
{
    [Fact]
    public async Task Process_run_csv_completes_with_artifact()
    {
        await using var db = CreateDb();
        var runId = Guid.NewGuid();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId,
            TenantId = TenantDefaults.DefaultTenantId,
            PeriodStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Draft
        });
        for (var i = 0; i < 3; i++)
        {
            db.SalaryLines.Add(new SalaryLine
            {
                TenantId = TenantDefaults.DefaultTenantId,
                PayrollRunId = runId,
                EmployeeId = Guid.NewGuid(),
                GrossPay = 1000 + i,
                TotalDeductions = 10,
                NetPay = 990 + i
            });
        }
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var user = User(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var (ok, jobId, err) = await svc.EnqueueAsync(ExportJobTemplates.PayrollRunCsv, new { RunId = runId }, user);
        Assert.True(ok, err);

        await svc.ProcessAsync(jobId!.Value);

        var job = await svc.GetAsync(jobId.Value);
        Assert.Equal(ExportJobStatus.Completed, job!.Status);
        Assert.Equal(3, job.ProcessedCount);
        Assert.True(job.ResultFileId.HasValue);

        var (dlOk, stream, _, _, dlErr) = await svc.OpenDownloadAsync(jobId.Value, user);
        Assert.True(dlOk, dlErr);
        await using (stream)
        {
            using var reader = new StreamReader(stream!, Encoding.UTF8);
            var text = await reader.ReadToEndAsync();
            Assert.Contains("EmployeeId,GrossPay", text);
        }
    }

    [Fact]
    public async Task Download_denied_for_different_user()
    {
        await using var db = CreateDb();
        var svc = CreateService(db);
        var ownerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        var otherId = Guid.Parse("33333333-3333-4333-8333-333333333333");

        var runId = Guid.NewGuid();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId,
            TenantId = TenantDefaults.DefaultTenantId,
            PeriodStart = DateTime.UtcNow.Date,
            PeriodEnd = DateTime.UtcNow.Date,
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Draft
        });
        await db.SaveChangesAsync();

        var (ok, jobId, _) = await svc.EnqueueAsync(ExportJobTemplates.PayrollRunCsv, new { RunId = runId }, User(ownerId));
        Assert.True(ok);
        await svc.ProcessAsync(jobId!.Value);

        var (dlOk, _, _, _, error) = await svc.OpenDownloadAsync(jobId.Value, User(otherId));
        Assert.False(dlOk);
        Assert.Contains("غير مصرح", error);
    }

    [Fact]
    public async Task Cancel_marks_job_cancelled()
    {
        await using var db = CreateDb();
        var svc = CreateService(db);
        var userId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        var (ok, jobId, _) = await svc.EnqueueAsync(ExportJobTemplates.PayrollEosCsv, new { }, User(userId));
        Assert.True(ok);

        var (cancelOk, _) = await svc.CancelAsync(jobId!.Value, userId);
        Assert.True(cancelOk);
        Assert.Equal(ExportJobStatus.Cancelled, (await svc.GetAsync(jobId.Value))!.Status);
    }

    [Fact]
    public void ExportAuth_rejects_cross_tenant()
    {
        var snapshot = new ExportAuthSnapshot
        {
            UserId = Guid.Parse("55555555-5555-4555-8555-555555555555"),
            TenantId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")
        };
        Assert.False(ExportJobAuth.MatchesCurrentUser(snapshot, User(snapshot.UserId),
            new FixedTenant(Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"))));
    }

    [Fact]
    public void Chunk_size_default_is_bounded()
    {
        var opts = new ExportJobOptions();
        Assert.InRange(opts.ChunkSize, 1, 5000);
        Assert.Equal(15, opts.DownloadPresignMinutes);
    }

    private static PayrollExportJobService CreateService(PayrollDbContext db) =>
        new(db, new NoopPublish(), new LocalExportArtifactStore(new TestHostEnvironment()),
            Options.Create(new ExportJobOptions { ChunkSize = 2 }), DesignTimeTenantContext.Instance);

    private static ClaimsPrincipal User(Guid id)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, id.ToString("D")));
        return new ClaimsPrincipal(identity);
    }

    private static PayrollDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new PayrollDbContext(options, DesignTimeTenantContext.Instance);
    }

    private sealed class FixedTenant(Guid id) : ITenantContext
    {
        public Guid? TenantId => id;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => id;
    }

    private sealed class NoopPublish : IPublishEndpoint
    {
        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotSupportedException();
        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish(object message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "perf004-" + Guid.NewGuid().ToString("N"));
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
