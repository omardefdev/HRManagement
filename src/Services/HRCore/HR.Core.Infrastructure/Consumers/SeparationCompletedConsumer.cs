using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Infrastructure.Consumers;

public class SeparationCompletedConsumer : IConsumer<ISeparationCompleted>
{
    private readonly HRCoreDbContext _db;
    private readonly EmployeeIntegrationService _integration;
    private readonly ILogger<SeparationCompletedConsumer> _logger;

    public SeparationCompletedConsumer(
        HRCoreDbContext db,
        EmployeeIntegrationService integration,
        ILogger<SeparationCompletedConsumer> logger)
    {
        _db = db;
        _integration = integration;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ISeparationCompleted> context)
    {
        var m = context.Message;
        _logger.LogInformation("HR.Core: SeparationCompleted EmployeeId={EmployeeId}", m.EmployeeId);

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == m.EmployeeId, context.CancellationToken);
        if (employee is null)
        {
            _logger.LogWarning("HR.Core: SeparationCompleted — employee {EmployeeId} not found", m.EmployeeId);
            return;
        }

        var note = $"SeparationCompleted Type={m.SeparationType} At={m.EffectiveDate:yyyy-MM-dd}";
        await AppendApprovedCvNoteAsync(m.EmployeeId, note, context.CancellationToken);

        _db.EmploymentRecords.Add(new EmploymentRecord
        {
            EmployeeId = m.EmployeeId,
            PositionTitle = "إنهاء خدمة",
            StartDate = m.EffectiveDate,
            EmploymentType = EmploymentType.Permanent,
            Notes = note
        });

        var targetStatus = EmploymentStatusRules.MapSeparationType(m.SeparationType);
        var oldStatus = employee.CurrentStatus;
        var history = new List<EmploymentStatusHistory>();
        var (ok, error) = EmployeeLifecycleService.TryChangeStatus(
            employee,
            targetStatus,
            note,
            changedBy: null,
            history);

        if (!ok)
        {
            _logger.LogWarning(
                "HR.Core: SeparationCompleted status transition rejected EmployeeId={EmployeeId}: {Error}",
                m.EmployeeId, error);
        }
        else
        {
            foreach (var h in history)
                _db.EmploymentStatusHistories.Add(h);
        }

        await _db.SaveChangesAsync(context.CancellationToken);

        if (ok)
        {
            await _integration.PublishStatusChangedIfNeededAsync(
                employee,
                oldStatus,
                targetStatus,
                note,
                context.CancellationToken);
        }
    }

    internal static async Task AppendApprovedCvNoteAsync(HRCoreDbContext db, Guid employeeId, string note, CancellationToken ct)
    {
        var cv = await db.ApprovedCVs
            .Where(c => c.EmployeeId == employeeId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (cv is null) return;
        cv.Notes = string.IsNullOrWhiteSpace(cv.Notes)
            ? note
            : $"{cv.Notes} | {note}";
    }

    private Task AppendApprovedCvNoteAsync(Guid employeeId, string note, CancellationToken ct) =>
        AppendApprovedCvNoteAsync(_db, employeeId, note, ct);
}
