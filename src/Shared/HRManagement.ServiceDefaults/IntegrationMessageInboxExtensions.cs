using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

public static class IntegrationMessageInboxExtensions
{
    public const string ProcessedOutcome = "Processed";
    public const string DuplicateOutcome = "DuplicateSkip";

    public static void ConfigureIntegrationMessageReceipts(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IntegrationMessageReceipt>(entity =>
        {
            entity.ToTable("IntegrationMessageReceipts");
            entity.HasIndex(e => new { e.TenantId, e.MessageId, e.ConsumerType })
                .IsUnique()
                .HasDatabaseName("IX_IntegrationMessageReceipts_Tenant_Message_Consumer");
            entity.Property(e => e.MessageId).HasMaxLength(128);
            entity.Property(e => e.ConsumerType).HasMaxLength(200);
            entity.Property(e => e.Outcome).HasMaxLength(50);
        });
    }

    public static Guid ResolveInboxTenantId(ITenantContext? tenantContext = null)
    {
        var id = tenantContext?.TenantId ?? TenantIdFlow.Get();
        if (!id.HasValue || id.Value == Guid.Empty)
            throw new InvalidOperationException(
                "تعذر تحديد المستأجر لمعالجة الرسالة — رأس X-Tenant-ID مطلوب.");
        return id.Value;
    }

    public static string ResolveConsumerType(ConsumeContext context)
    {
        var path = context.ReceiveContext.InputAddress?.AbsolutePath?.Trim('/') ?? "";
        if (!string.IsNullOrEmpty(path))
        {
            var segment = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
            if (!string.IsNullOrWhiteSpace(segment))
                return segment;
        }

        throw new InvalidOperationException("تعذر تحديد نوع المستهلك لطابور الاستقبال.");
    }

    public static string RequireMessageId(ConsumeContext context)
    {
        var messageId = context.MessageId?.ToString();
        if (string.IsNullOrWhiteSpace(messageId))
            throw new InvalidOperationException("رسالة بلا MessageId — رفض مغلق (INT-002).");
        return messageId;
    }

    public static Task<bool> HasReceiptAsync(
        this DbContext db,
        string messageId,
        string consumerType,
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        db.Set<IntegrationMessageReceipt>().AnyAsync(
            r => r.MessageId == messageId
                 && r.ConsumerType == consumerType
                 && r.TenantId == tenantId,
            cancellationToken);

    /// <summary>Stages a receipt on the change tracker without saving (same TX as business).</summary>
    public static IntegrationMessageReceipt StageReceipt(
        this DbContext db,
        string messageId,
        string consumerType,
        Guid tenantId,
        string outcome = ProcessedOutcome)
    {
        var receipt = new IntegrationMessageReceipt
        {
            MessageId = messageId,
            ConsumerType = consumerType,
            TenantId = tenantId,
            ReceivedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow,
            Outcome = outcome
        };
        db.Set<IntegrationMessageReceipt>().Add(receipt);
        return receipt;
    }

    public static async Task<bool> ShouldSkipAsync(
        this DbContext db,
        ConsumeContext context,
        string consumerType,
        CancellationToken cancellationToken = default)
    {
        var messageId = context.MessageId?.ToString();
        if (string.IsNullOrEmpty(messageId))
            return false;

        Guid tenantId;
        try
        {
            tenantId = ResolveInboxTenantId();
        }
        catch (InvalidOperationException)
        {
            return await db.Set<IntegrationMessageReceipt>()
                .AnyAsync(r => r.MessageId == messageId && r.ConsumerType == consumerType, cancellationToken);
        }

        return await db.HasReceiptAsync(messageId, consumerType, tenantId, cancellationToken);
    }

    /// <summary>Legacy helper — prefer inbox filter. Stages then saves (not atomic with prior business).</summary>
    public static async Task TryRecordAsync(
        this DbContext db,
        ConsumeContext context,
        string consumerType,
        CancellationToken cancellationToken = default)
    {
        var messageId = context.MessageId?.ToString();
        if (string.IsNullOrEmpty(messageId))
            return;

        Guid tenantId;
        try
        {
            tenantId = ResolveInboxTenantId();
        }
        catch (InvalidOperationException)
        {
            tenantId = Guid.Empty;
        }

        db.StageReceipt(messageId, consumerType, tenantId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Duplicate MessageId race on redelivery — safe to ignore when used outside filter.
        }
    }

    public static async Task<bool> ShouldSkipAsync(
        this IIntegrationMessageInbox inbox,
        ConsumeContext context,
        string consumerType,
        CancellationToken cancellationToken = default)
    {
        var messageId = context.MessageId?.ToString();
        if (string.IsNullOrEmpty(messageId))
            return false;

        return await inbox.HasProcessedAsync(messageId, consumerType, cancellationToken);
    }

    public static Task TryRecordAsync(
        this IIntegrationMessageInbox inbox,
        ConsumeContext context,
        string consumerType,
        CancellationToken cancellationToken = default)
    {
        var messageId = context.MessageId?.ToString();
        if (string.IsNullOrEmpty(messageId))
            return Task.CompletedTask;

        return inbox.TryRecordAsync(messageId, consumerType, cancellationToken);
    }

    public static async Task ExecuteInInboxTransactionAsync(
        DbContext db,
        ConsumeContext context,
        string consumerType,
        Func<CancellationToken, Task> processAsync,
        ILogger? logger = null,
        InboxMetrics? metrics = null,
        CancellationToken cancellationToken = default)
    {
        var messageId = RequireMessageId(context);
        var tenantId = ResolveInboxTenantId();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await db.HasReceiptAsync(messageId, consumerType, tenantId, cancellationToken))
            {
                metrics?.DuplicateSkip.Add(1);
                logger?.LogDebug(
                    "Inbox skip duplicate {ConsumerType} MessageId={MessageId} Tenant={TenantId}",
                    consumerType, messageId, tenantId);
                await tx.CommitAsync(cancellationToken);
                return;
            }

            await processAsync(cancellationToken);

            db.StageReceipt(messageId, consumerType, tenantId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                metrics?.Processed.Add(1);
            }
            catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                metrics?.UniqueConflict.Add(1);
                logger?.LogInformation(
                    "Inbox unique conflict {ConsumerType} MessageId={MessageId} — rolled back",
                    consumerType, messageId);
            }
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
