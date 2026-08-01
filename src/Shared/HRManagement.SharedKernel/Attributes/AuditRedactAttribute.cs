namespace HRManagement.SharedKernel;

/// <summary>
/// Marks a property whose values must not appear in EntityAuditLog.ChangedFields.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditRedactAttribute : Attribute
{
}
