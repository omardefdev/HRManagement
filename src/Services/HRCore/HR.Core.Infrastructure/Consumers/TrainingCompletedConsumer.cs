using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;

namespace HR.Core.Infrastructure.Consumers;

// INT-002: inbox handled by InboxConsumeFilter
public class TrainingCompletedConsumer : IConsumer<ITrainingCompleted>
{
    private readonly HRCoreDbContext _db;
    private readonly ILogger<TrainingCompletedConsumer> _logger;

    public TrainingCompletedConsumer(HRCoreDbContext db, ILogger<TrainingCompletedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ITrainingCompleted> context)
    {
        var m = context.Message;
        if (!EventSchemaVersion.Supports(m.SchemaVersion, supportedMax: 3, _logger, nameof(ITrainingCompleted)))
            return;

        _logger.LogInformation(
            "HR.Core: TrainingCompleted EmployeeId={EmployeeId} CourseId={CourseId}",
            m.EmployeeId, m.CourseId);

        var courseKey = m.CourseId.ToString();
        if (await _db.Qualifications.AnyAsync(q =>
                q.EmployeeId == m.EmployeeId && q.QualificationCode == courseKey, context.CancellationToken))
        {
            return;
        }

        _db.Qualifications.Add(new Qualification
        {
            EmployeeId = m.EmployeeId,
            QualificationCode = courseKey,
            Title = $"تدريب مكتمل — الدرجة {m.Score}",
            Provider = "نظام التدريب",
            CompletionDate = m.CompletedAt,
            FileId = m.CertificateFileId
        });

        var trainingNote = $"TrainingCompleted Course={m.CourseId} Score={m.Score}";
        await SeparationCompletedConsumer.AppendApprovedCvNoteAsync(_db, m.EmployeeId, trainingNote, context.CancellationToken);
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
