using Compliance.Domain.Entities;

using Compliance.Infrastructure.Data;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;

using HRManagement.MessageContracts.Events;

using HRManagement.ServiceDefaults;

using HRManagement.SharedKernel.Enums;



namespace Compliance.Infrastructure.Consumers;



public class MedicalRecommendationIssuedConsumer : IConsumer<IMedicalRecommendationIssued>

{

    private readonly ComplianceDbContext _db;

    private readonly ILogger<MedicalRecommendationIssuedConsumer> _logger;



    public MedicalRecommendationIssuedConsumer(ComplianceDbContext db, ILogger<MedicalRecommendationIssuedConsumer> logger)

    {

        _db = db;

        _logger = logger;

    }



    public async Task Consume(ConsumeContext<IMedicalRecommendationIssued> context)

    {

        var m = context.Message;

        if (!EventSchemaVersion.Supports(m.SchemaVersion, supportedMax: 2, _logger, nameof(IMedicalRecommendationIssued)))

            return;



        _logger.LogInformation(

            "Compliance received MedicalRecommendationIssued: EmployeeId={EmployeeId}, Type={RecommendationType}",

            m.EmployeeId, m.RecommendationType);



        if (m.RecommendationType is not "Retirement" and not "Compensation")

            return;



        var marker = ConsumerMessageMarkers.MedicalRecommendation(m.RecommendationId);

        if (await _db.AuditFindings.AnyAsync(f => f.Description != null && f.Description.Contains(marker), context.CancellationToken))

            return;



        var finding = new AuditFinding

        {

            Id = Guid.NewGuid(),

            Description = $"توصية طبية ({m.RecommendationType}) للموظف {m.EmployeeId} — {marker}",

            Severity = Compliance.Domain.Enums.FindingSeverity.High,

            Status = Compliance.Domain.Enums.FindingStatus.Open

        };

        _db.AuditFindings.Add(finding);



        _db.ComplianceRecommendations.Add(new ComplianceRecommendation

        {

            Id = Guid.NewGuid(),

            FindingId = finding.Id,

            RecommendedAction = $"متابعة التوصية الطبية: {m.RecommendationType} — RecommendationId={m.RecommendationId}",

            DueDate = DateTime.UtcNow.AddDays(30),

            Status = ApprovalStatus.Pending

        });



        await _db.SaveChangesAsync(context.CancellationToken);

    }

}

