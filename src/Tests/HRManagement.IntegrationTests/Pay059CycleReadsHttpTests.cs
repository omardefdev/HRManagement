using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.OrgScope;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-59 (P-9): the reads an SQL-free payroll cycle needs, over HTTP on Payroll.Web with its real pipeline and PostgreSQL.
/// <c>GET api/payroll/runs/{id}</c> answers the run's present version, the approvals of that version (given by different approvers, an
/// earlier version's not counted) and required (two above the agency's threshold), and the posting's disbursement batch (null before the
/// posting). <c>GET api/payroll/payment-batches/{id}/orders</c> answers each order by employee — state, amount, hold reason, whether a
/// destination is pinned, the sequence of the file that carried it — and never an account number or a field named like one. Both are
/// 404 for another agency; the orders read is not the reviewer's (<c>Module.Payroll</c>).
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay059CycleReadsHttpTests(PostgreSqlFixture postgres)
{
    private static readonly Guid Tenant = TenantDefaults.DefaultTenantId;
    private static readonly Guid OtherAgency = Guid.Parse("59000000-0000-4000-8000-0000000000b0");
    private static readonly string ProcessorRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollProcessor}";
    private static readonly string ApproverRoles = $"{HrManagementRoles.Payroll},{HrManagementRoles.PayrollApprover}";

    private static async Task<Dictionary<Type, Guid>> SeedAsync(PayrollDbContext db, Guid unit)
    {
        var employee = Guid.NewGuid();
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            Id = employee,
            TenantId = Tenant,
            EmployeeId = employee,
            BaseSalary = 4500m,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true,
            OrgUnitId = unit,
        });
        await db.SaveChangesAsync();
        return new Dictionary<Type, Guid> { [typeof(EmployeePayProfile)] = employee };
    }

    private Task<Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>> StartAsync() =>
        Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext>.StartAsync(
            postgres, "PayrollDb", PayrollOrgScopeKeys.Registry, ProcessorRoles, ApproverRoles, "/EmployeePayProfiles/Create", SeedAsync,
            settings: new Dictionary<string, string?> { ["Seed:Profile"] = "None" });

    private static HttpClient As(Iam052PgServiceHost<PayrollWebProgram, PayrollDbContext> host, string roles, Guid? agency = null)
    {
        var client = host.Host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, (agency ?? Tenant).ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString("D"));
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        return client;
    }

    /// <summary>Every property name in a JSON document, at any depth.</summary>
    private static IEnumerable<string> Names(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Names(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Names),
        _ => [],
    };

    private static void AssertNoAccount(string body, params string[] numbers)
    {
        foreach (var number in numbers)
            Assert.DoesNotContain(number, body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        Assert.DoesNotContain(Names(json.RootElement), name =>
            name.Contains("account", StringComparison.OrdinalIgnoreCase) || name.Contains("iban", StringComparison.OrdinalIgnoreCase)
            || name.Contains("beneficiary", StringComparison.OrdinalIgnoreCase));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_run_read_answers_its_version_approvals_and_batch_and_the_orders_read_answers_each_order_without_an_account()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var paid = host.Records[typeof(EmployeePayProfile)].Own;
        var held = host.Records[typeof(EmployeePayProfile)].Foreign;
        var number = Pay055TestAccounts.Iban(paid);
        var start = new DateTime(2031, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstApprover = Guid.NewGuid();
        var secondApprover = Guid.NewGuid();
        var earlierApprover = Guid.NewGuid();

        // A posted run of 60,000,000 (above the threshold: two approvals) at its second version, with one approval of the first version
        // (which no longer counts) and two of the second; its batch, one order sent by the first file and one held with no account.
        var posted = new PayrollRun
        {
            TenantId = Tenant,
            PeriodStart = start,
            PeriodEnd = start.AddMonths(1).AddDays(-1),
            RunDate = start.AddMonths(1),
            Status = PayrollRunStatus.Posted,
            TotalNetPay = 60_000_000m,
        };
        var draft = new PayrollRun
        {
            TenantId = Tenant,
            PeriodStart = start.AddMonths(1),
            PeriodEnd = start.AddMonths(2).AddDays(-1),
            RunDate = start.AddMonths(2),
            TotalNetPay = 900m,
        };
        var batch = new PaymentBatch
        {
            TenantId = Tenant,
            PayrollRunId = posted.Id,
            TotalAmount = 60_000_000m,
            Status = PaymentBatchStatus.Submitted,
            Source = PaymentBatchSource.PayrollRunPosted,
        };
        var file = new PaymentBatchFile
        {
            TenantId = Tenant,
            PaymentBatchId = batch.Id,
            Sequence = 1,
            Kind = PaymentBatchFileKind.Initial,
            ControlCount = 1,
            ControlTotal = 59_999_000m,
            Sha256 = new string('b', 64),
            Content = [1],
            GeneratedBy = Guid.NewGuid(),
            GeneratedAt = DateTime.UtcNow,
        };
        var paidLine = Guid.NewGuid();
        var heldLine = Guid.NewGuid();
        await host.WithDbAsync(async db =>
        {
            db.Add(posted);
            db.Add(draft);
            db.Entry(posted).Property(nameof(PayrollRun.Revision)).CurrentValue = 2;
            db.AddRange(
                new PayrollRunApproval { TenantId = Tenant, PayrollRunId = posted.Id, ApproverId = earlierApprover, Decision = ApprovalDecision.Approved, Revision = 1, DecidedAt = DateTime.UtcNow },
                new PayrollRunApproval { TenantId = Tenant, PayrollRunId = posted.Id, ApproverId = firstApprover, Decision = ApprovalDecision.Approved, Revision = 2, DecidedAt = DateTime.UtcNow },
                new PayrollRunApproval { TenantId = Tenant, PayrollRunId = posted.Id, ApproverId = secondApprover, Decision = ApprovalDecision.Approved, Revision = 2, DecidedAt = DateTime.UtcNow });
            db.Add(batch);
            db.Add(file);
            db.AddRange(
                new PaymentOrder
                {
                    TenantId = Tenant,
                    PayrollRunId = posted.Id,
                    PaymentBatchId = batch.Id,
                    SalaryLineId = paidLine,
                    EmployeeId = paid,
                    Amount = 59_999_000m,
                    BeneficiaryIban = number,
                    Status = PaymentOrderStatus.Submitted,
                    PaymentBatchFileId = file.Id,
                    IdempotencyKey = PaymentOrder.IdempotencyKeyFor(posted.Id, paidLine),
                },
                new PaymentOrder
                {
                    TenantId = Tenant,
                    PayrollRunId = posted.Id,
                    PaymentBatchId = batch.Id,
                    SalaryLineId = heldLine,
                    EmployeeId = held,
                    Amount = 1_000m,
                    Status = PaymentOrderStatus.Held,
                    HoldReason = PaymentOrderHoldReasons.NoAccount,
                    IdempotencyKey = PaymentOrder.IdempotencyKeyFor(posted.Id, heldLine),
                });
            await db.SaveChangesAsync();
            return true;
        });

        using var processor = As(host, ProcessorRoles);

        // The run read.
        using (var response = await processor.GetAsync($"/api/payroll/runs/{posted.Id:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            AssertNoAccount(body, number);
            using var json = JsonDocument.Parse(body);
            var run = json.RootElement;
            Assert.Equal((2, 2, 2, batch.Id),
                (run.GetProperty("revision").GetInt32(), run.GetProperty("approvalsGiven").GetInt32(), run.GetProperty("approvalsRequired").GetInt32(),
                 run.GetProperty("paymentBatchId").GetGuid()));
        }

        using (var response = await processor.GetAsync($"/api/payroll/runs/{draft.Id:D}"))
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var run = json.RootElement;
            Assert.Equal((1, 0, 1, JsonValueKind.Null),
                (run.GetProperty("revision").GetInt32(), run.GetProperty("approvalsGiven").GetInt32(), run.GetProperty("approvalsRequired").GetInt32(),
                 run.GetProperty("paymentBatchId").ValueKind));
        }

        // The orders read.
        using (var response = await processor.GetAsync($"/api/payroll/payment-batches/{batch.Id:D}/orders"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            AssertNoAccount(body, number);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            Assert.Equal((batch.Id, 2, 1), (root.GetProperty("batchId").GetGuid(), root.GetProperty("total").GetInt32(), root.GetProperty("page").GetInt32()));
            var orders = root.GetProperty("orders").EnumerateArray().ToDictionary(o => o.GetProperty("employeeId").GetGuid());
            Assert.Equal(new[] { paid, held }.Order(), root.GetProperty("orders").EnumerateArray().Select(o => o.GetProperty("employeeId").GetGuid()));
            var sent = orders[paid];
            Assert.Equal(("Submitted", 59_999_000m, true, 1, paidLine, JsonValueKind.Null),
                (sent.GetProperty("status").GetString(), sent.GetProperty("amount").GetDecimal(), sent.GetProperty("destinationPinned").GetBoolean(),
                 sent.GetProperty("fileSequence").GetInt32(), sent.GetProperty("salaryLineId").GetGuid(), sent.GetProperty("holdReason").ValueKind));
            var kept = orders[held];
            Assert.Equal(("Held", 1_000m, false, PaymentOrderHoldReasons.NoAccount, JsonValueKind.Null),
                (kept.GetProperty("status").GetString(), kept.GetProperty("amount").GetDecimal(), kept.GetProperty("destinationPinned").GetBoolean(),
                 kept.GetProperty("holdReason").GetString(), kept.GetProperty("fileSequence").ValueKind));
        }

        // A page past the end is empty; an unknown batch is 404.
        using (var response = await processor.GetAsync($"/api/payroll/payment-batches/{batch.Id:D}/orders?page=2"))
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            Assert.Equal(0, json.RootElement.GetProperty("orders").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await processor.GetAsync($"/api/payroll/payment-batches/{Guid.NewGuid():D}/orders")).StatusCode);

        // Another agency: neither the run nor the batch exists for it.
        using var foreigner = As(host, ProcessorRoles, OtherAgency);
        Assert.Equal(HttpStatusCode.NotFound, (await foreigner.GetAsync($"/api/payroll/runs/{posted.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreigner.GetAsync($"/api/payroll/payment-batches/{batch.Id:D}/orders")).StatusCode);

        // The independent reviewer reads the run he reviews, and not its batch's orders.
        using var auditor = As(host, HrManagementRoles.Auditor);
        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync($"/api/payroll/runs/{posted.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await auditor.GetAsync($"/api/payroll/payment-batches/{batch.Id:D}/orders")).StatusCode);
    }
}
