namespace HRManagement.MessageContracts.Events;

public interface IEmployeeCreated
{
    Guid EmployeeId { get; }
    string FullName { get; }

    /// <summary>
    /// SHA-256 of the national identifier, not the identifier itself.
    /// <para>
    /// The raw number used to travel on this event and was then persisted by every subscriber —
    /// copied into analytics payloads, embedded in IT ticket descriptions, and used as a directory
    /// username. That spread the most sensitive identifier the system holds across four databases
    /// that have no business holding it. Subscribers that need to correlate an employee across
    /// services can compare hashes; nothing downstream needs the number itself.
    /// </para>
    /// </summary>
    string NationalIdHash { get; }

    string EmploymentType { get; }
    Guid? TenantId { get; }
    Guid? PrimaryOrgUnitId { get; }

    int SchemaVersion => 3;
}
