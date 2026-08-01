using Admin.Logistics.Domain.Enums;

using Admin.Logistics.Infrastructure.Data;

using HRManagement.MessageContracts.Events;

using HRManagement.ServiceDefaults;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;



namespace Admin.Logistics.Infrastructure.Consumers;



// INT-002: inbox handled by InboxConsumeFilter

public class SeparationCompletedConsumer : IConsumer<ISeparationCompleted>

{

    private readonly AdminLogisticsDbContext _db;

    private readonly ILogger<SeparationCompletedConsumer> _logger;



    public SeparationCompletedConsumer(AdminLogisticsDbContext db, ILogger<SeparationCompletedConsumer> logger)

    {

        _db = db;

        _logger = logger;

    }



    public async Task Consume(ConsumeContext<ISeparationCompleted> context)

    {

        var emp = context.Message.EmployeeId;

        if (!EventSchemaVersion.Supports(context.Message.SchemaVersion, supportedMax: 2, _logger, nameof(ISeparationCompleted)))

            return;



        _logger.LogInformation("Admin.Logistics: SeparationCompleted EmployeeId={EmployeeId}", emp);



        var open = await _db.AssetAssignments

            .Where(a => a.EmployeeId == emp && a.ReturnedAt == null)

            .ToListAsync(context.CancellationToken);



        foreach (var a in open)

        {

            a.ReturnedAt = DateTime.UtcNow;

            var asset = await _db.Assets.FirstOrDefaultAsync(x => x.Id == a.AssetId, context.CancellationToken);

            if (asset is not null)

                asset.Status = AssetStatus.InStorage;

        }



        if (open.Count > 0)

            await _db.SaveChangesAsync(context.CancellationToken);

    }

}
