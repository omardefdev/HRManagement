using System.Collections.Concurrent;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using File.Storage.Domain.Entities;
using File.Storage.Domain.Enums;
using File.Storage.Infrastructure.Data;
using File.Storage.Infrastructure.Services;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HR.Movements.Domain.Entities;
using HR.Movements.Infrastructure.Data;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>DATA-001 parallel concurrency suite on real PostgreSQL (requires RUN_TESTCONTAINERS=true).</summary>
public class Data001ConcurrencyMatrixTests : IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private PostgreSqlContainer? _postgres;

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_TESTCONTAINERS"), "true", StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        if (!Enabled) return;
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    private string Cs => _postgres!.GetConnectionString();
    private static FixedTenantContext Tenant() => new(TenantId);

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task SalaryLine_ConcurrentInsert_SecondFailsUnique()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = await MigratePayrollAsync();
        var run = NewRun();
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();

        var employeeId = Guid.NewGuid();
        var errors = new ConcurrentBag<Exception>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 2), async (i, ct) =>
        {
            await using var local = new PayrollDbContext(NpgsqlOpts<PayrollDbContext>(), Tenant());
            local.SalaryLines.Add(new SalaryLine
            {
                TenantId = TenantId,
                PayrollRunId = run.Id,
                EmployeeId = employeeId,
                GrossPay = 1000 + i,
                TotalDeductions = 0,
                NetPay = 1000 + i
            });
            try { await local.SaveChangesAsync(ct); }
            catch (Exception ex) { errors.Add(ex); }
        });

        Assert.Single(errors);
        Assert.All(errors, e => Assert.True(ConcurrencyExtensions.IsUniqueViolation(e)));
        Assert.Equal(1, await db.SalaryLines.CountAsync(x => x.PayrollRunId == run.Id && x.EmployeeId == employeeId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task PayrollRun_ConcurrentCreate_SamePeriod_OneWins()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = await MigratePayrollAsync();
        var start = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);
        var errors = new ConcurrentBag<Exception>();

        await Parallel.ForEachAsync(Enumerable.Range(0, 2), async (_, ct) =>
        {
            await using var local = new PayrollDbContext(NpgsqlOpts<PayrollDbContext>(), Tenant());
            local.PayrollRuns.Add(new PayrollRun
            {
                TenantId = TenantId,
                PeriodStart = start,
                PeriodEnd = end,
                RunDate = DateTime.UtcNow,
                Status = PayrollRunStatus.Draft,
                RunType = PayrollRunType.Regular
            });
            try { await local.SaveChangesAsync(ct); }
            catch (Exception ex) { errors.Add(ex); }
        });

        Assert.Single(errors);
        Assert.Equal(1, await db.PayrollRuns.CountAsync(r =>
            r.PeriodStart == start && r.PeriodEnd == end && r.Status != PayrollRunStatus.Reversed));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task FileReference_ConcurrentRegister_CountConsistent()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        var options = NpgsqlOpts<FileStorageDbContext>();
        var fileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        await using (var setup = new FileStorageDbContext(options, Tenant()))
        {
            await setup.Database.MigrateAsync();
            setup.Files.Add(new FileRecord
            {
                Id = fileId,
                TenantId = TenantId,
                OriginalName = "a.pdf",
                Extension = ".pdf",
                MimeType = "application/pdf",
                Size = 10,
                Bucket = "file-storage",
                ObjectKey = "a/obj",
                UploadedAt = DateTime.UtcNow,
                Status = FileRecordStatus.Pending,
                ReferenceCount = 0
            });
            await setup.SaveChangesAsync();
        }

        var storage = new NoOpStorage();
        await Parallel.ForEachAsync(Enumerable.Range(0, 8), async (i, ct) =>
        {
            await using var db = new FileStorageDbContext(options, Tenant());
            await new FilePlatformService(
                db,
                storage,
                new EicarAwareScanner(),
                new ContentDisarmService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ContentDisarmService>.Instance))
                .RegisterReferenceAsync(fileId, $"holder-{i}", ct);
        });

        await using var verify = new FileStorageDbContext(options, Tenant());
        var file = await verify.Files.AsNoTracking().SingleAsync(f => f.Id == fileId);
        Assert.Equal(8, file.ReferenceCount);
        Assert.Equal(8, await verify.FileReferences.CountAsync(r => r.FileId == fileId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task LeaveBalance_ConcurrentDebit_NoOverdraw()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        var options = NpgsqlOpts<ContractsDbContext>();
        Guid balanceId;
        await using (var setup = new ContractsDbContext(options, Tenant()))
        {
            await setup.Database.MigrateAsync();
            var bal = new LeaveBalance
            {
                TenantId = TenantId,
                EmployeeId = Guid.NewGuid(),
                LeaveType = LeaveType.Annual,
                Year = 2026,
                TotalEntitlement = 5,
                Used = 0
            };
            setup.LeaveBalances.Add(bal);
            await setup.SaveChangesAsync();
            balanceId = bal.Id;
        }

        await Parallel.ForEachAsync(Enumerable.Range(0, 10), async (_, ct) =>
        {
            await using var db = new ContractsDbContext(options, Tenant());
            await db.LeaveBalances
                .Where(b => b.Id == balanceId && b.Used + 1 <= b.TotalEntitlement)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Used, b => b.Used + 1), ct);
        });

        await using var verify = new ContractsDbContext(options, Tenant());
        Assert.Equal(5, await verify.LeaveBalances.Where(b => b.Id == balanceId).Select(b => b.Used).SingleAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Transfer_ConcurrentExecute_OneSucceeds()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        var options = NpgsqlOpts<MovementsDbContext>();
        Guid transferId;
        await using (var setup = new MovementsDbContext(options, Tenant()))
        {
            await setup.Database.MigrateAsync();
            var t = new TransferRequest
            {
                TenantId = TenantId,
                EmployeeId = Guid.NewGuid(),
                FromOrgUnitId = Guid.NewGuid(),
                ToOrgUnitId = Guid.NewGuid(),
                RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Status = ApprovalStatus.Approved
            };
            setup.TransferRequests.Add(t);
            await setup.SaveChangesAsync();
            transferId = t.Id;
        }

        var wins = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, 4), async (_, ct) =>
        {
            await using var db = new MovementsDbContext(options, Tenant());
            var n = await db.TransferRequests
                .Where(x => x.Id == transferId && x.Status == ApprovalStatus.Approved)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ApprovalStatus.Executed), ct);
            if (n == 1) Interlocked.Increment(ref wins);
        });

        Assert.Equal(1, wins);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Inventory_ConcurrentDecrement_NoNegative()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        var options = NpgsqlOpts<AdminLogisticsDbContext>();
        Guid itemId;
        await using (var setup = new AdminLogisticsDbContext(options, Tenant()))
        {
            await setup.Database.MigrateAsync();
            var item = new InventoryItem
            {
                TenantId = TenantId,
                Sku = "SKU-1",
                Name = "Paper",
                Quantity = 3,
                Unit = "box"
            };
            setup.InventoryItems.Add(item);
            await setup.SaveChangesAsync();
            itemId = item.Id;
        }

        await Parallel.ForEachAsync(Enumerable.Range(0, 10), async (_, ct) =>
        {
            await using var db = new AdminLogisticsDbContext(options, Tenant());
            await db.InventoryItems
                .Where(x => x.Id == itemId && x.Quantity >= 1)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - 1), ct);
        });

        await using var verify = new AdminLogisticsDbContext(options, Tenant());
        Assert.Equal(0, await verify.InventoryItems.Where(x => x.Id == itemId).Select(x => x.Quantity).SingleAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task PayrollRun_ConcurrentStaleWrite_SecondGetsConcurrencyConflict()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = await MigratePayrollAsync();
        var run = NewRun();
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();

        await using var ctx1 = new PayrollDbContext(NpgsqlOpts<PayrollDbContext>(), Tenant());
        await using var ctx2 = new PayrollDbContext(NpgsqlOpts<PayrollDbContext>(), Tenant());
        var r1 = await ctx1.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        var r2 = await ctx2.PayrollRuns.SingleAsync(r => r.Id == run.Id);

        r1.TotalNetPay = 1001m;
        r2.TotalNetPay = 1002m;
        await ctx1.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctx2.SaveChangesAsync());
        Assert.True(ConcurrencyExtensions.IsConcurrencyConflict(ex));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task IdempotencyKey_DuplicateKey_FailsUnique()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");
        await using var db = await MigratePayrollAsync();
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            TenantId = TenantId,
            Key = "k1",
            RequestHash = "abc",
            ResponseStatus = 200,
            ResponseBody = "{\"ok\":true}"
        });
        await db.SaveChangesAsync();

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            TenantId = TenantId,
            Key = "k1",
            RequestHash = "different",
            ResponseStatus = 200
        });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(ConcurrencyExtensions.IsUniqueViolation(ex));
    }

    private PayrollRun NewRun() => new()
    {
        TenantId = TenantId,
        PeriodStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEnd = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
        RunDate = DateTime.UtcNow,
        Status = PayrollRunStatus.Draft,
        RunType = PayrollRunType.Regular
    };

    private async Task<PayrollDbContext> MigratePayrollAsync()
    {
        var db = new PayrollDbContext(NpgsqlOpts<PayrollDbContext>(), Tenant());
        await db.Database.MigrateAsync();
        return db;
    }

    private DbContextOptions<T> NpgsqlOpts<T>() where T : DbContext =>
        new DbContextOptionsBuilder<T>()
            .UseNpgsql(Cs, npgsql => npgsql.MigrationsAssembly(typeof(T).Assembly.GetName().Name))
            .Options;

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }

    private sealed class NoOpStorage : IFileStorageService
    {
        public Task EnsureBucketsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<FileReference> UploadAsync(string bucket, string prefix, Stream content, string fileName, string contentType, long contentLength, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FileReference> UploadFormFileAsync(string bucket, string prefix, IFormFile file, FileUploadConstraints constraints, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetPresignedDownloadUrlAsync(FileReference reference, TimeSpan? expiry = null, CancellationToken cancellationToken = default) => Task.FromResult("http://x");
        public Task<string> GetPresignedUploadUrlAsync(string bucket, string objectKey, string contentType, TimeSpan? expiry = null, CancellationToken cancellationToken = default) => Task.FromResult("http://x");
        public Task<Stream> OpenReadAsync(FileReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(FileReference reference, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(FileReference reference, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlyList<string>> ListObjectKeysAsync(string bucket, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<FileReference> PutAsync(string bucket, string objectKey, Guid fileId, Stream content, string contentType, long contentLength, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileReference(fileId, bucket, objectKey, contentType, contentLength, "abc"));
    }
}
