using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Infrastructure.Data;
using HRManagement.MessageContracts.Events;

namespace IDCard.Infrastructure.Consumers;

public class EmployeeCreatedConsumer : IConsumer<IEmployeeCreated>
{
    private readonly IDCardDbContext _db;
    private readonly ILogger<EmployeeCreatedConsumer> _logger;

    public EmployeeCreatedConsumer(IDCardDbContext db, ILogger<EmployeeCreatedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IEmployeeCreated> context)
    {
        var m = context.Message;
        _logger.LogInformation("IDCard: EmployeeCreated EmployeeId={EmployeeId}", m.EmployeeId);

        // PLAN-46: a draft, a submitted or an approved request is still open; the hire adds none beside it. The request stays a draft
        // (IdCard:Approval:AutoSubmitOnHire=false): nobody vouches for a request the bus made.
        var exists = await _db.CardRequests.AnyAsync(r =>
            r.EmployeeId == m.EmployeeId
            && !r.IsCancelled
            && (r.Status == CardRequestStatus.Pending || r.Status == CardRequestStatus.PendingApproval || r.Status == CardRequestStatus.Approved));
        if (exists) return;

        _db.CardRequests.Add(new CardRequest
        {
            EmployeeId = m.EmployeeId,
            RequestDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Purpose = CardRequestPurpose.New,
            Status = CardRequestStatus.Pending
        });
        await _db.SaveChangesAsync();
    }
}
