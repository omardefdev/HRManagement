namespace HRManagement.SharedKernel.Entities;

/// <summary>
/// Local replica of employee primary org unit for BOLA checks in satellite modules (IAM-002).
/// </summary>
public class EmployeeOrgUnitSnapshot
{
    public Guid EmployeeId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? PrimaryOrgUnitId { get; set; }
}
