using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Consumers;
using Auth.Directory.Infrastructure.Data;
using Auth.Directory.Infrastructure.Keycloak;
using HR.Health.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-52 (§5, D-52-4, P-10): a transferred restricted user loses his scope (fail-closed). With the Keycloak sync on, the attribute's
/// removal is requested from Keycloak (a fake admin API) and his other attributes go back unchanged; with it off, a warning and
/// <c>directory_scope_sync_skipped_total</c>; the local scope is empty in both, the audit row holds the old unit, the new one and the transfer,
/// and a redelivery adds nothing. HR.Health's snapshot follows the transfer, once.
/// </summary>
public sealed class Iam052DirectoryTransferConsumerTests
{
    private static readonly Guid Tenant = Guid.Parse("a0000052-0000-4000-8000-0000000000f1");
    private static readonly Guid OldScope = Guid.Parse("a0000052-0000-4000-8000-0000000000f2");
    private static readonly Guid From = Guid.Parse("a0000052-0000-4000-8000-0000000000f3");
    private static readonly Guid To = Guid.Parse("a0000052-0000-4000-8000-0000000000f4");

    private sealed record Transfer(Guid EmployeeId, Guid FromOrgUnitId, Guid ToOrgUnitId, DateTime ExecutedAt, Guid? TransferRequestId, Guid? TenantId)
        : ITransferExecuted;

    /// <summary>The Keycloak admin API: a token endpoint, a user with three attributes, and the PUT it receives.</summary>
    private sealed class FakeKeycloak : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            if (request.RequestUri.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
                return Json("""{"access_token":"t","expires_in":60}""");
            if (request.Method == HttpMethod.Get)
                return Json($$$"""{"id":"kc-1","username":"mgr","attributes":{"tenant_id":["{{{Tenant}}}"],"employee_id":["e"],"scope_org_unit_id":["{{{OldScope}}}"]}}""");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static IConfiguration Configuration(bool syncEnabled) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Keycloak:DirectorySync:Enabled"] = syncEnabled ? "true" : "false",
        ["Keycloak:DirectorySync:AdminClientId"] = "directory-sync",
        ["Keycloak:DirectorySync:AdminClientSecret"] = nameof(Iam052DirectoryTransferConsumerTests),
        ["Keycloak:Authority"] = "http://keycloak.test/realms/hrmanagement",
    }).Build();

    private static async Task<(AuthDirectoryDbContext Db, AuthUser User)> SeedAsync(string name)
    {
        var db = new AuthDirectoryDbContext(new DbContextOptionsBuilder<AuthDirectoryDbContext>().UseInMemoryDatabase(name).Options);
        var user = new AuthUser
        {
            TenantId = Tenant,
            Username = "mgr",
            Email = "mgr@test",
            PasswordHash = "-",
            EmployeeId = Guid.NewGuid(),
            ScopeOrgUnitId = OldScope,
            KeycloakUserId = "kc-1",
            IsActive = true
        };
        db.AuthUsers.Add(user);
        await db.SaveChangesAsync();
        return (db, user);
    }

    private static ConsumeContext<ITransferExecuted> Context(ITransferExecuted message, Guid messageId) =>
        Mock.Of<ConsumeContext<ITransferExecuted>>(c => c.Message == message && c.MessageId == messageId && c.CancellationToken == CancellationToken.None);

    [Fact]
    public async Task With_the_sync_on_the_attribute_is_removed_in_Keycloak_the_local_scope_cleared_and_audited_once()
    {
        var (db, user) = await SeedAsync($"iam052-on-{Guid.NewGuid():N}");
        await using var _ = db;
        var keycloak = new FakeKeycloak();
        var configuration = Configuration(syncEnabled: true);
        var provisioning = new KeycloakUserProvisioningService(new HttpClient(keycloak), configuration, NullLogger<KeycloakUserProvisioningService>.Instance);
        var consumer = new TransferExecutedConsumer(db, NullLogger<TransferExecutedConsumer>.Instance, provisioning, configuration);
        var transfer = new Transfer(user.EmployeeId!.Value, From, To, DateTime.UtcNow, Guid.NewGuid(), Tenant);

        await consumer.Consume(Context(transfer, Guid.NewGuid()));
        // A redelivery of the same transfer (another message id) changes nothing and writes no second audit row.
        await consumer.Consume(Context(transfer, Guid.NewGuid()));

        Assert.Null((await db.AuthUsers.AsNoTracking().SingleAsync()).ScopeOrgUnitId);
        var audit = Assert.Single(await db.AuthAuditLogs.AsNoTracking().Where(a => a.Action == TransferExecutedConsumer.ScopeClearedAction).ToListAsync());
        Assert.Contains(OldScope.ToString(), audit.Details);
        Assert.Contains(To.ToString(), audit.Details);
        Assert.Contains(transfer.TransferRequestId!.Value.ToString(), audit.Details);
        var put = Assert.Single(keycloak.Requests, r => r.Method == HttpMethod.Put);
        Assert.EndsWith("/admin/realms/hrmanagement/users/kc-1", put.Path);
        Assert.DoesNotContain("scope_org_unit_id", put.Body);
        Assert.Contains("tenant_id", put.Body);
        Assert.Contains("employee_id", put.Body);
    }

    [Fact]
    public async Task With_the_sync_off_a_warning_and_the_counter_and_the_local_scope_is_cleared()
    {
        var (db, user) = await SeedAsync($"iam052-off-{Guid.NewGuid():N}");
        await using var _ = db;
        var keycloak = new FakeKeycloak();
        var configuration = Configuration(syncEnabled: false);
        var provisioning = new KeycloakUserProvisioningService(new HttpClient(keycloak), configuration, NullLogger<KeycloakUserProvisioningService>.Instance);
        var consumer = new TransferExecutedConsumer(db, NullLogger<TransferExecutedConsumer>.Instance, provisioning, configuration);

        long skipped = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "directory_scope_sync_skipped_total")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref skipped, value));
        listener.Start();

        var transfer = new Transfer(user.EmployeeId!.Value, From, To, DateTime.UtcNow, null, Tenant);
        var messageId = Guid.NewGuid();
        await consumer.Consume(Context(transfer, messageId));
        await consumer.Consume(Context(transfer, messageId)); // the same message again (no transfer id: the message id is the key)

        Assert.Null((await db.AuthUsers.AsNoTracking().SingleAsync()).ScopeOrgUnitId);
        Assert.Single(await db.AuthAuditLogs.AsNoTracking().Where(a => a.Action == TransferExecutedConsumer.ScopeClearedAction).ToListAsync());
        Assert.Empty(keycloak.Requests);
        Assert.Equal(1, Interlocked.Read(ref skipped));
    }

    [Fact]
    public async Task A_user_without_a_scope_is_left_as_he_is()
    {
        var (db, user) = await SeedAsync($"iam052-none-{Guid.NewGuid():N}");
        await using var _ = db;
        user.ScopeOrgUnitId = null;
        await db.SaveChangesAsync();
        var consumer = new TransferExecutedConsumer(db, NullLogger<TransferExecutedConsumer>.Instance, null, Configuration(syncEnabled: true));
        await consumer.Consume(Context(new Transfer(user.EmployeeId!.Value, From, To, DateTime.UtcNow, Guid.NewGuid(), Tenant), Guid.NewGuid()));
        Assert.Empty(await db.AuthAuditLogs.AsNoTracking().Where(a => a.Action == TransferExecutedConsumer.ScopeClearedAction).ToListAsync());
    }

    [Fact]
    public async Task A_failed_removal_in_Keycloak_throws_so_the_message_retries_and_nothing_is_saved()
    {
        var (db, user) = await SeedAsync($"iam052-fail-{Guid.NewGuid():N}");
        await using var _ = db;
        var keycloak = new Mock<IKeycloakUserProvisioningService>();
        keycloak.Setup(k => k.TryClearScopeAttributeAsync("kc-1", It.IsAny<CancellationToken>())).ReturnsAsync(KeycloakProvisionResult.Failed("down"));
        var consumer = new TransferExecutedConsumer(db, NullLogger<TransferExecutedConsumer>.Instance, keycloak.Object, Configuration(syncEnabled: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            consumer.Consume(Context(new Transfer(user.EmployeeId!.Value, From, To, DateTime.UtcNow, Guid.NewGuid(), Tenant), Guid.NewGuid())));
        db.ChangeTracker.Clear();
        Assert.Equal(OldScope, (await db.AuthUsers.AsNoTracking().SingleAsync()).ScopeOrgUnitId);
    }

    [Fact]
    public async Task HR_Health_snapshot_follows_the_transfer_once()
    {
        await using var db = new HealthDbContext(new DbContextOptionsBuilder<HealthDbContext>().UseInMemoryDatabase($"iam052-health-{Guid.NewGuid():N}").Options);
        var employee = Guid.NewGuid();
        db.EmployeeOrgUnitSnapshots.Add(new EmployeeOrgUnitSnapshot { EmployeeId = employee, TenantId = Tenant, PrimaryOrgUnitId = From });
        await db.SaveChangesAsync();
        var consumer = new HR.Health.Infrastructure.Consumers.TransferExecutedConsumer(db, NullLogger<HR.Health.Infrastructure.Consumers.TransferExecutedConsumer>.Instance);
        var first = new Transfer(employee, From, To, DateTime.UtcNow, Guid.NewGuid(), Tenant);
        var second = new Transfer(employee, To, OldScope, DateTime.UtcNow, Guid.NewGuid(), Tenant);

        await consumer.Consume(Context(first, Guid.NewGuid()));
        Assert.Equal(To, (await db.EmployeeOrgUnitSnapshots.AsNoTracking().SingleAsync()).PrimaryOrgUnitId);
        await consumer.Consume(Context(second, Guid.NewGuid()));
        // A late redelivery of the first transfer does not move the snapshot back.
        await consumer.Consume(Context(first, Guid.NewGuid()));
        Assert.Equal(OldScope, (await db.EmployeeOrgUnitSnapshots.AsNoTracking().SingleAsync()).PrimaryOrgUnitId);
        Assert.Equal(2, await db.IntegrationMessageReceipts.CountAsync(r => r.ConsumerType == HR.Health.Infrastructure.Consumers.TransferExecutedConsumer.ReceiptConsumer));
    }
}
