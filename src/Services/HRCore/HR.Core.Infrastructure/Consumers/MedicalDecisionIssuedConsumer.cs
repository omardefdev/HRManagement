using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Consumers;

// INT-002: inbox handled by InboxConsumeFilter
public class MedicalDecisionIssuedConsumer : IConsumer<IMedicalDecisionIssued>
{
    private readonly HRCoreDbContext _db;
    private readonly ILogger<MedicalDecisionIssuedConsumer> _logger;

    public MedicalDecisionIssuedConsumer(HRCoreDbContext db, ILogger<MedicalDecisionIssuedConsumer> logger)
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
        var marker = MedicalEmploymentEffectRules.BuildHistoryMarker("MedicalDecision", Guid.TryParse(refKey, out var gid) ? gid : m.EmployeeId);
        if (await _db.EmploymentStatusHistories.AnyAsync(
                h => h.EmployeeId == m.EmployeeId && h.Reason != null && h.Reason.Contains(marker),
                context.CancellationToken))
        {
            return;
        }

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == m.EmployeeId, context.CancellationToken);
        if (employee is null)
        {
            _logger.LogWarning("MedicalDecisionIssued: employee {EmployeeId} not found", m.EmployeeId);
            return;
        }

        _logger.LogInformation(
            "HR.Core: MedicalDecisionIssued EmployeeId={EmployeeId} Ref={MedicalRef}",
            m.EmployeeId, m.MedicalRef);

        var detail = m.Notes ?? m.MedicalRef;
        var reason = MedicalEmploymentEffectRules.BuildReason(marker, detail, m.SourceSystem);
        var newStatus = MedicalEmploymentEffectRules.ResolveStatusFromMedicalRef(m.MedicalRef);

        if (newStatus.HasValue)
        {
            var history = new List<EmploymentStatusHistory>();
            var (ok, error) = EmployeeLifecycleService.TryChangeStatus(
                employee, newStatus.Value, reason, changedBy: null, history);
            if (!ok)
            {
                _logger.LogWarning("MedicalDecisionIssued: status change failed for {EmployeeId}: {Error}",
                    m.EmployeeId, error);
            }
            else
            {
                foreach (var entry in history)
                    _db.EmploymentStatusHistories.Add(entry);
            }
        }
        else
        {
            _db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
            {
                EmployeeId = m.EmployeeId,
                OldStatus = employee.CurrentStatus.ToString(),
                NewStatus = employee.CurrentStatus.ToString(),
                ChangedAt = m.EffectiveDate,
                Reason = reason
            });
        }

        if (MedicalEmploymentEffectRules.RequiresLimitedDutiesRecordFromRef(m.MedicalRef))
        {
            _db.EmploymentRecords.Add(new EmploymentRecord
            {
                EmployeeId = m.EmployeeId,
                PositionTitle = "مهام محدودة (لجنة طبية)",
                StartDate = m.EffectiveDate.Date,
                EmploymentType = EmploymentType.Permanent,
                Notes = reason
            });
        }

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
