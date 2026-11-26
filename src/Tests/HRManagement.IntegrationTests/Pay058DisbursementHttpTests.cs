using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.OrgScope;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-58 over HTTP on Payroll.Web itself (PostgreSQL): the bank file is downloaded as it was stored, by the poster's or the approver's
/// role only, and each download is a bulk export in the security audit that names the file, never an account.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay058DisbursementHttpTests(PostgreSqlFixture postgres, ITestOutputHelper output)
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly string Poster = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollPoster}";
    private static readonly string Approver = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}";
    private static readonly string Processor = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor}";
    private const string Account = "SY00PLAN58TESTACCOUNT0001";

    private sealed class RecordingAudit : ISecurityAuditSink
    {
        public ConcurrentQueue<SecurityAuditPayload> Events { get; } = new();

        public bool TryPublish(SecurityAuditPayload payload)
        {
            Events.Enqueue(payload);
            return true;
        }
    }

    private static async Task<Dictionary<Type, Guid>> SeedAsync(PayrollDbContext db, Guid unit)
    {
        var employee = Guid.NewGuid();
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            Id = employee,
            TenantId = Tenant,
            EmployeeId = employee,
            BaseSalary = 4500m,
            IsActive = true,
            OrgUnitId = unit,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
        return new Dictionary<Type, Guid> { [typeof(EmployeePayProfile)] = employee };
    }

    private Task<Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>> StartAsync(RecordingAudit audit) =>
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>.StartAsync(
            postgres, "PayrollDb", PayrollOrgScopeKeys.Registry, Poster, Poster, "/EmployeePayProfiles", SeedAsync,
            testServices: services =>
            {
                services.RemoveAll<ISecurityAuditSink>();
                services.AddSingleton<ISecurityAuditSink>(audit);
            });

    private static async Task<(Guid BatchId, PaymentBatchFile File)> StoredFileAsync(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host) =>
        await host.WithDbAsync(async db =>
        {
            var run = new PayrollRun
            {
                TenantId = Tenant,
                PeriodStart = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2030, 1, 31, 0, 0, 0, DateTimeKind.Utc),
                RunDate = new DateTime(2030, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = PayrollRunStatus.Posted,
                TotalNetPay = 100m,
            };
            db.Add(run);
            var batch = new PaymentBatch { TenantId = Tenant, PayrollRunId = run.Id, TotalAmount = 100m, Status = PaymentBatchStatus.Submitted };
            db.Add(batch);
            var file = new PaymentBatchFile
            {
                TenantId = Tenant,
                PaymentBatchId = batch.Id,
                Sequence = 1,
                Kind = PaymentBatchFileKind.Initial,
                ControlCount = 1,
                ControlTotal = 100m,
                Sha256 = new string('a', 64),
                Content = System.Text.Encoding.UTF8.GetBytes($"header\nrow,{Account},100.00\nCONTROL,1,100.00\n"),
                GeneratedBy = Guid.NewGuid(),
                GeneratedAt = DateTime.UtcNow,
            };
            db.Add(file);
            await db.SaveChangesAsync();
            return (batch.Id, file);
        });

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_bank_file_is_read_as_stored_by_the_poster_or_the_approver_and_each_download_is_audited_without_an_account()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        var audit = new RecordingAudit();
        await using var host = await StartAsync(audit);
        var (batchId, file) = await StoredFileAsync(host);
        var path = $"/api/payroll/payment-batches/{batchId}/files/{file.Id}";

        foreach (var roles in new[] { Poster, Approver })
        {
            using var client = host.CentralAs(Guid.NewGuid(), roles);
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(file.Content, await response.Content.ReadAsByteArrayAsync());
        }

        using (var processor = host.CentralAs(Guid.NewGuid(), Processor))
        using (var refused = await processor.GetAsync(path))
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var exports = audit.Events.Where(e => e.EventType == SecurityAudit.EventTypes.BulkExport).ToList();
        Assert.Equal(2, exports.Count);
        Assert.All(exports, e =>
        {
            Assert.Equal($"payment-batch-file:{file.Id:N}", e.Resource);
            Assert.DoesNotContain(Account, e.DetailJson ?? "", StringComparison.Ordinal);
            Assert.Equal(1, JsonDocument.Parse(e.DetailJson!).RootElement.GetProperty("sequence").GetInt32());
        });

        // The stored bytes are what is read: nothing built a second file.
        Assert.Equal(1, await host.WithDbAsync(db => db.Set<PaymentBatchFile>().CountAsync(f => f.PaymentBatchId == batchId)));
        output.WriteLine($"downloads audited: {exports.Count}");
    }
}
