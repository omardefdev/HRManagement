namespace HRManagement.ServiceDefaults;

/// <summary>
/// Stable idempotency markers for integration consumers that lack a dedicated receipt table.
/// </summary>
public static class ConsumerMessageMarkers
{
    public static string EmployeeCreated(Guid employeeId) => $"[EmployeeCreated:{employeeId:N}]";

    public static string SeparationCompleted(Guid employeeId, Guid? separationRecordId) =>
        separationRecordId.HasValue
            ? $"[SeparationCompleted:{employeeId:N}:{separationRecordId:N}]"
            : $"[SeparationCompleted:{employeeId:N}]";

    public static string MedicalRecommendation(Guid recommendationId) =>
        $"MedicalRecommendation-{recommendationId:N}";

    public static string DisciplinaryAction(Guid actionId) =>
        $"DisciplinaryAction-{actionId:N}";

    public static string LeaveApproved(Guid leaveRequestId) => $"LeaveApproved:{leaveRequestId}";

    public static string AdvanceApproved(Guid advanceRequestId) => $"AdvanceApproved:{advanceRequestId}";

    public static string AnalyticsEvent(string eventName, string referenceKey) =>
        $"{{\"referenceKey\":\"{eventName}:{referenceKey}\"}}";
}
