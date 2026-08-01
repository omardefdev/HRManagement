namespace HRManagement.MessageContracts.Events;

public interface ICardIssued
{
    Guid EmployeeId { get; }
    Guid IssuedCardId { get; }
    string SerialNumber { get; }
    DateTime IssuedAt { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
