using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults;

public static class MassTransitOutboxModelBuilderExtensions
{
    public static void ConfigureMassTransitOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
