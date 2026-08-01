namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>
/// Marks a controller action or handler as requiring dual (four-eyes) approval before commit (IAM-003).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequiresDualApprovalAttribute : Attribute
{
    public RequiresDualApprovalAttribute(string entityType)
    {
        EntityType = entityType;
    }

    public string EntityType { get; }

    /// <summary>Optional financial amount property name on the request model.</summary>
    public string? AmountProperty { get; set; }
}
