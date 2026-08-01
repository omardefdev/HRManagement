namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>
/// Detects sensitive legal-field changes that require four-eyes approval (IAM-003).
/// </summary>
public static class LegalDataChangeGuard
{
    public static readonly HashSet<string> ProtectedEmployeeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "NationalId",
        "DateOfBirth",
        "EmploymentDate",
        "HireDate",
        "FullName",
        "FirstName",
        "LastName"
    };

    public static bool IsProtectedField(string fieldName) =>
        ProtectedEmployeeFields.Contains(fieldName);

    public static SodResult ValidateChange(
        Guid actorId,
        Guid? secondApproverId,
        IEnumerable<string> changedFields)
    {
        var protectedChanges = changedFields.Where(IsProtectedField).ToList();
        if (protectedChanges.Count == 0)
            return SodResult.Ok();

        if (!secondApproverId.HasValue)
            return SodResult.Deny(
                $"تعديل البيانات القانونية ({string.Join(", ", protectedChanges)}) يتطلب اعتماداً مزدوجاً.");

        if (secondApproverId.Value == actorId)
            return SodResult.Deny("يتطلب تعديل البيانات القانونية موافقة شخص ثانٍ مختلف.");

        return SodResult.Ok();
    }

    /// <summary>
    /// Creates a pending dual-approval request for a legal data change when immediate dual approval is unavailable.
    /// </summary>
    public static bool RequiresApprovalRequest(IEnumerable<string> changedFields) =>
        changedFields.Any(IsProtectedField);
}
