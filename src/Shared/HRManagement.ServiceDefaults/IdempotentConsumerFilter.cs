using System.Diagnostics.Metrics;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults;

/// <summary>INT-002 metrics for inbox processing.</summary>
public sealed class InboxMetrics
{
    public static readonly string MeterName = "HRManagement.Inbox";

    private readonly Meter _meter;

    public InboxMetrics(IMeterFactory? meterFactory = null)
    {
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        DuplicateSkip = _meter.CreateCounter<long>("inbox_duplicate_skip");
        Processed = _meter.CreateCounter<long>("inbox_processed");
        UniqueConflict = _meter.CreateCounter<long>("inbox_unique_conflict");
        PermanentFault = _meter.CreateCounter<long>("consumer_fault_permanent");
        Retry = _meter.CreateCounter<long>("consumer_retry");
    }

    public Counter<long> DuplicateSkip { get; }
    public Counter<long> Processed { get; }
    public Counter<long> UniqueConflict { get; }
    public Counter<long> PermanentFault { get; }
    public Counter<long> Retry { get; }
}

/// <summary>
/// INT-002: wraps consumer execution in one DB transaction with inbox receipt.
/// Consumer type is resolved from the receive endpoint name (MassTransit ConfigureEndpoints).
/// </summary>
public sealed class InboxConsumeFilter<T>(
    IServiceProvider serviceProvider,
    ILogger<InboxConsumeFilter<T>> logger,
    InboxMetrics metrics,
    ITenantContext? tenantContext = null) : IFilter<ConsumeContext<T>>
    where T : class
{
    public void Probe(ProbeContext context) => context.CreateFilterScope("inboxConsume");

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var db = serviceProvider.GetService<DbContext>();
        if (db is null
            || db.Model.FindEntityType(typeof(HRManagement.SharedKernel.Entities.IntegrationMessageReceipt)) is null)
        {
            await next.Send(context);
            return;
        }

        var messageId = IntegrationMessageInboxExtensions.RequireMessageId(context);
        var consumerType = IntegrationMessageInboxExtensions.ResolveConsumerType(context);
        var tenantId = tenantContext is not null
            ? IntegrationMessageInboxExtensions.ResolveInboxTenantId(tenantContext)
            : IntegrationMessageInboxExtensions.ResolveInboxTenantId();

        await using var tx = await db.Database.BeginTransactionAsync(context.CancellationToken);
        try
        {
            if (await db.HasReceiptAsync(messageId, consumerType, tenantId, context.CancellationToken))
            {
                metrics.DuplicateSkip.Add(1);
                logger.LogDebug(
                    "Inbox skip duplicate {ConsumerType} MessageId={MessageId} Tenant={TenantId}",
                    consumerType, messageId, tenantId);
                await tx.CommitAsync(context.CancellationToken);
                return;
            }

            await next.Send(context);

            db.StageReceipt(messageId, consumerType, tenantId);
            try
            {
                await db.SaveChangesAsync(context.CancellationToken);
                await tx.CommitAsync(context.CancellationToken);
                metrics.Processed.Add(1);
            }
            catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(context.CancellationToken);
                db.ChangeTracker.Clear();
                metrics.UniqueConflict.Add(1);
                logger.LogInformation(
                    "Inbox unique conflict {ConsumerType} MessageId={MessageId} — business rolled back",
                    consumerType, messageId);
            }
        }
        catch
        {
            await tx.RollbackAsync(context.CancellationToken);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
