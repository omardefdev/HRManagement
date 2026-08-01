namespace HRManagement.MessageContracts.Events;



public interface IDisciplinaryActionTaken

{

    Guid EmployeeId { get; }

    string ActionType { get; }

    DateTime IssuedAt { get; }

    string? Notes { get; }

    Guid? ComplaintId { get; }

    Guid? CorrelationId { get; }

    string SourceSystem { get; }

    int PenaltyDays { get; }

    Guid ActionId { get; }



    Guid? TenantId => null;




    int SchemaVersion => 1;

}

