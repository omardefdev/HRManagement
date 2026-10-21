using File.Storage.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-05 AC-16: File.Storage inbox idempotency on PostgreSQL.</summary>
[Collection("PostgreSql")]
public class FileStorageInboxIdempotencyTests(PostgreSqlFixture postgres)
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task FileStorage_Inbox_Duplicate_Message_Single_Effect()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true");

        var cs = await postgres.CreateDatabaseAsync(TestDatabaseNames.Unique("file_inbox_"));
        var options = new DbContextOptionsBuilder<FileStorageDbContext>()
            .UseNpgsql(cs, n => n.MigrationsAssembly(typeof(FileStorageDbContext).Assembly.GetName().Name))
            .Options;

        await using var migrate = new FileStorageDbContext(options, new FixedTenant(TenantId));
        await migrate.Database.MigrateAsync();

        await using var db = new FileStorageDbContext(options, new FixedTenant(TenantId));
        var messageId = NewId.NextGuid().ToString("D");
        const string consumer = "FileStorageTestConsumer";

        await RunOnceAsync(db, messageId, consumer);
        await RunOnceAsync(db, messageId, consumer);

        Assert.Equal(1, await db.FileReferences.CountAsync());
        Assert.Equal(1, await db.IntegrationMessageReceipts.CountAsync(r =>
            r.MessageId == messageId && r.TenantId == TenantId));
    }

    private static async Task RunOnceAsync(FileStorageDbContext db, string messageId, string consumer)
    {
        TenantIdFlow.Set(TenantId);
        try
        {
            var ctx = new Mock<ConsumeContext>();
            ctx.SetupGet(c => c.MessageId).Returns(Guid.Parse(messageId));
            ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

            await IntegrationMessageInboxExtensions.ExecuteInInboxTransactionAsync(
                db,
                ctx.Object,
                consumer,
                async ct =>
                {
                    if (!await db.FileReferences.AnyAsync(r => r.HolderKey == "inbox-test", ct))
                    {
                        var file = new File.Storage.Domain.Entities.FileRecord
                        {
                            TenantId = TenantId,
                            Bucket = "test",
                            ObjectKey = "inbox-test-key",
                            OriginalName = "test.bin",
                            Extension = "bin",
                            MimeType = "application/octet-stream",
                            Size = 1,
                            UploadedAt = DateTime.UtcNow,
                            Status = File.Storage.Domain.Enums.FileRecordStatus.Active
                        };
                        db.Files.Add(file);
                        await db.SaveChangesAsync(ct);
                        db.FileReferences.Add(new File.Storage.Domain.Entities.FileReferenceRecord
                        {
                            TenantId = TenantId,
                            FileId = file.Id,
                            HolderKey = "inbox-test"
                        });
                        await db.SaveChangesAsync(ct);
                    }
                });
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
