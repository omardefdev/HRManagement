using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

public static class MassTransitOutboxModelBuilderExtensions
{
    public static void ConfigureMassTransitOutbox(this ModelBuilder modelBuilder)
    {
        // PLAN-40: InboxState with its keys: the consumer outbox relies on the (MessageId, ConsumerId) alternate key.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
