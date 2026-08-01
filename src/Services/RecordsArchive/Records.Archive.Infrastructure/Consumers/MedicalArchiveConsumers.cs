using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;
using Records.Archive.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;

namespace Records.Archive.Infrastructure.Consumers;

public class MedicalRecommendationIssuedArchiveConsumer : IConsumer<IMedicalRecommendationIssued>
{
    private readonly ArchiveDbContext _db;
    private readonly ILogger<MedicalRecommendationIssuedArchiveConsumer> _logger;

    public MedicalRecommendationIssuedArchiveConsumer(ArchiveDbContext db, ILogger<MedicalRecommendationIssuedArchiveConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IMedicalRecommendationIssued> context)
    {
        var m = context.Message;
        if (!EventSchemaVersion.Supports(m.SchemaVersion, supportedMax: 2, _logger, nameof(IMedicalRecommendationIssued)))
            return;

        var reference = $"MED-REC-{m.RecommendationId}";
        if (await _db.ArchiveItems.AnyAsync(x => x.ReferenceNumber == reference, context.CancellationToken))
            return;

        _logger.LogInformation(
            "Records.Archive: MedicalRecommendationIssued EmployeeId={EmployeeId} Ref={Reference}",
            m.EmployeeId, reference);

        _db.ArchiveItems.Add(new ArchiveItem
        {
            Title = $"ملف طبي — توصية {m.RecommendationType} للموظف {m.EmployeeId}: {m.Details}",
            ReferenceNumber = reference,
            Format = ArchiveFormat.Digital,
            StorageLocation = "HR.Health/Recommendations",
            RetentionPeriodYears = 15
        });

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}

public class MedicalDecisionIssuedArchiveConsumer : IConsumer<IMedicalDecisionIssued>
{
    private readonly ArchiveDbContext _db;
    private readonly ILogger<MedicalDecisionIssuedArchiveConsumer> _logger;

    public MedicalDecisionIssuedArchiveConsumer(ArchiveDbContext db, ILogger<MedicalDecisionIssuedArchiveConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IMedicalDecisionIssued> context)
    {
        var m = context.Message;
        if (!EventSchemaVersion.Supports(m.SchemaVersion, supportedMax: 1, _logger, nameof(IMedicalDecisionIssued)))
            return;

        var refKey = m.CorrelationId ?? m.MedicalRef;
        var reference = $"MED-DEC-{refKey}";
        if (await _db.ArchiveItems.AnyAsync(x => x.ReferenceNumber == reference, context.CancellationToken))
            return;

        _logger.LogInformation(
            "Records.Archive: MedicalDecisionIssued EmployeeId={EmployeeId} Ref={Reference}",
            m.EmployeeId, reference);

        _db.ArchiveItems.Add(new ArchiveItem
        {
            Title = $"ملف طبي — قرار لجنة {m.MedicalRef} للموظف {m.EmployeeId}: {m.Notes}",
            ReferenceNumber = reference,
            Format = ArchiveFormat.Digital,
            StorageLocation = "HR.Health/CommitteeReports",
            RetentionPeriodYears = 15
        });

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
