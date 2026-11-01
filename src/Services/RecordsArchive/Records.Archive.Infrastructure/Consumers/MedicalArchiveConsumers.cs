using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using MassTransit;
using Microsoft.Extensions.Logging;
using Records.Archive.Infrastructure.Data;

namespace Records.Archive.Infrastructure.Consumers;

public class MedicalRecommendationIssuedArchiveConsumer(
    ArchiveDbContext db,
    ICodeGenerationClient codeClient,
    ILogger<MedicalRecommendationIssuedArchiveConsumer> logger) : IConsumer<IMedicalRecommendationIssued>
{
    public async Task Consume(ConsumeContext<IMedicalRecommendationIssued> context)
    {
        var m = context.Message;
        EventSchemaVersion.Require(m.SchemaVersion, supportedMax: 2, nameof(IMedicalRecommendationIssued));

        var title = $"ملف طبي — توصية {m.RecommendationType} للموظف {m.EmployeeId}: {m.Details} [{m.RecommendationId:N}]";
        if (await ArchiveReferenceFactory.ExistsByTitleAsync(db, title, context.CancellationToken))
            return;

        var reference = await ArchiveReferenceFactory.CreateReferenceAsync<IMedicalRecommendationIssued>(codeClient, "MEDREC", context.CancellationToken);

        // No employee id next to a medical event in the logs (CodeQL cs/exposure-of-sensitive-information): the archive
        // reference identifies the item.
        logger.LogInformation("Records.Archive: MedicalRecommendationIssued archived Ref={Reference}", reference);

        db.ArchiveItems.Add(ArchiveReferenceFactory.CreateItem(
            title, reference, "HR.Health/Recommendations", retentionYears: 15));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

public class MedicalDecisionIssuedArchiveConsumer(
    ArchiveDbContext db,
    ICodeGenerationClient codeClient,
    ILogger<MedicalDecisionIssuedArchiveConsumer> logger) : IConsumer<IMedicalDecisionIssued>
{
    public async Task Consume(ConsumeContext<IMedicalDecisionIssued> context)
    {
        var m = context.Message;
        EventSchemaVersion.Require(m.SchemaVersion, supportedMax: 1, nameof(IMedicalDecisionIssued));

        var refKey = m.CorrelationId ?? m.MedicalRef;
        var title = $"ملف طبي — قرار لجنة {m.MedicalRef} للموظف {m.EmployeeId}: {m.Notes} [{refKey}]";
        if (await ArchiveReferenceFactory.ExistsByTitleAsync(db, title, context.CancellationToken))
            return;

        var reference = await ArchiveReferenceFactory.CreateReferenceAsync<IMedicalDecisionIssued>(codeClient, "MEDDEC", context.CancellationToken);

        logger.LogInformation("Records.Archive: MedicalDecisionIssued archived Ref={Reference}", reference);

        db.ArchiveItems.Add(ArchiveReferenceFactory.CreateItem(
            title, reference, "HR.Health/CommitteeReports", retentionYears: 15));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
