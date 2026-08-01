using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Infrastructure.Consumers;

public class TransferExecutedConsumer : IConsumer<ITransferExecuted>
{
    private readonly HRCoreDbContext _db;
    private readonly ILogger<TransferExecutedConsumer> _logger;

    public TransferExecutedConsumer(HRCoreDbContext db, ILogger<TransferExecutedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ITransferExecuted> context)
    {
        var m = context.Message;
        _logger.LogInformation(
            "HR.Core: TransferExecuted EmployeeId={EmployeeId} ToOrgUnitId={ToOrgUnitId}",
            m.EmployeeId, m.ToOrgUnitId);

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == m.EmployeeId);
        if (emp is null)
            return;

        if (emp.PrimaryOrgUnitId == m.ToOrgUnitId)
            return;

        emp.PrimaryOrgUnitId = m.ToOrgUnitId;
        var note = $"TransferExecuted FromOrg={m.FromOrgUnitId} ToOrg={m.ToOrgUnitId} At={m.ExecutedAt:O}";
        _db.EmploymentRecords.Add(new EmploymentRecord
        {
            EmployeeId = m.EmployeeId,
            PositionTitle = "نقل وظيفي",
            OrgUnitId = m.ToOrgUnitId,
            StartDate = m.ExecutedAt.Date,
            EmploymentType = EmploymentType.Permanent,
            Notes = note
        });
        await SeparationCompletedConsumer.AppendApprovedCvNoteAsync(_db, m.EmployeeId, note, context.CancellationToken);
        await _db.SaveChangesAsync();
    }
}
