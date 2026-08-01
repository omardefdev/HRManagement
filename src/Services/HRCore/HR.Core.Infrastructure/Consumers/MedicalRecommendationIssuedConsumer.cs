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
public class MedicalRecommendationIssuedConsumer : IConsumer<IMedicalRecommendationIssued>
{
    private readonly HRCoreDbContext _db;
    private readonly ILogger<MedicalRecommendationIssuedConsumer> _logger;

    public MedicalRecommendationIssuedConsumer(HRCoreDbContext db, ILogger<MedicalRecommendationIssuedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IMedicalRecommendationIssued> context)
    {
        var m = context.Message;
        if (!EventSchemaVersion.Supports(m.SchemaVersion, supportedMax: 2, _logger, nameof(IMedicalRecommendationIssued)))
            return;

        var marker = MedicalEmploymentEffectRules.BuildHistoryMarker("MedicalRecommendation", m.RecommendationId);
        if (await _db.EmploymentStatusHistories.AnyAsync(
                h => h.EmployeeId == m.EmployeeId && h.Reason != null && h.Reason.Contains(marker),
                context.CancellationToken))
        {
            return;
        }

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == m.EmployeeId, context.CancellationToken);
        if (employee is null)
        {
            _logger.LogWarning("MedicalRecommendationIssued: employee {EmployeeId} not found", m.EmployeeId);
            return;
        }

        _logger.LogInformation(
            "HR.Core: MedicalRecommendationIssued EmployeeId={EmployeeId} Type={Type}",
            m.EmployeeId, m.RecommendationType);

        var reason = MedicalEmploymentEffectRules.BuildReason(marker, m.Details, m.SourceSystem);
        var newStatus = MedicalEmploymentEffectRules.ResolveStatusFromRecommendation(m.RecommendationType);

        if (newStatus.HasValue)
        {
            var history = new List<EmploymentStatusHistory>();
            var (ok, error) = EmployeeLifecycleService.TryChangeStatus(
                employee, newStatus.Value, reason, changedBy: null, history);
            if (!ok)
            {
                _logger.LogWarning("MedicalRecommendationIssued: status change failed for {EmployeeId}: {Error}",
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
                ChangedAt = m.IssuedAt,
                Reason = reason
            });
        }

        if (MedicalEmploymentEffectRules.RequiresLimitedDutiesRecord(m.RecommendationType))
        {
            _db.EmploymentRecords.Add(new EmploymentRecord
            {
                EmployeeId = m.EmployeeId,
                PositionTitle = "مهام محدودة (قرار طبي)",
                StartDate = m.IssuedAt.Date,
                EmploymentType = EmploymentType.Permanent,
                Notes = reason
            });
        }

        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
