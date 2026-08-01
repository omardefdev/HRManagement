using IT.Infrastructure.Domain.Entities;
using IT.Infrastructure.Domain.Enums;

namespace IT.Infrastructure.Domain.Services;

public static class TicketWorkflowRules
{
    public static bool CanAssign(TicketStatus status) =>
        status == TicketStatus.Open;

    public static bool CanResolve(TicketStatus status) =>
        status is TicketStatus.Open or TicketStatus.Assigned;

    public static bool CanClose(TicketStatus status) =>
        status == TicketStatus.Resolved;

    public static bool CanReopen(TicketStatus status) =>
        status is TicketStatus.Resolved or TicketStatus.Closed;

    public static bool IsEscalated(SecurityIncident incident) =>
        incident.Severity >= Severity.High && incident.Status == TicketStatus.Open;
}
