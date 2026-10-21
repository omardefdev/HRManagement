using System.Text.Json;
using HR.Core.Domain.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Services;

public static class EmployeeCorrectionService
{
    private static readonly HashSet<string> LegalFields = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Employee.NationalId),
        nameof(Employee.FirstName),
        nameof(Employee.MiddleName),
        nameof(Employee.LastName),
        nameof(Employee.FullName),
        nameof(Employee.BirthDate),
        nameof(Employee.HireDate),
        "DateOfBirth",
        "EmploymentDate"
    };

    private static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Employee.NationalId),
        nameof(Employee.FirstName),
        nameof(Employee.MiddleName),
        nameof(Employee.LastName),
        nameof(Employee.FullName),
        nameof(Employee.BirthDate),
        nameof(Employee.HireDate),
        nameof(Employee.Gender),
        nameof(Employee.MaritalStatus),
        nameof(Employee.CurrentStatus),
        "DateOfBirth",
        "EmploymentDate"
    };

    public static (EmployeeCorrection? Correction, string? Error) CreateRequest(
        Employee employee,
        IReadOnlyDictionary<string, JsonElement> proposedChanges,
        string? reason,
        Guid requestedBy)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (null, "سبب التصحيح مطلوب.");

        if (proposedChanges.Count == 0)
            return (null, "لا توجد حقول للتصحيح.");

        var before = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var after = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var touchesLegal = false;

        foreach (var (key, value) in proposedChanges)
        {
            if (!AllowedFields.Contains(key))
                return (null, $"الحقل غير مسموح للتصحيح: {key}");

            var canonical = CanonicalField(key);
            if (LegalFields.Contains(key) || LegalFields.Contains(canonical))
                touchesLegal = true;

            before[canonical] = ReadCurrent(employee, canonical);
            after[canonical] = JsonElementToClr(value);
        }

        var payload = JsonSerializer.Serialize(new { before, after });
        var correction = new EmployeeCorrection
        {
            EmployeeId = employee.Id,
            TenantId = employee.TenantId,
            ChangesJson = payload,
            Reason = reason.Trim(),
            RequestedBy = requestedBy,
            RequestedAt = DateTime.UtcNow,
            Status = ApprovalStatus.Pending,
            RequiresLegalApproval = touchesLegal
        };

        return (correction, null);
    }

    public static (bool Success, string? Error) ApplyApproved(
        Employee employee,
        EmployeeCorrection correction,
        Guid approvedBy,
        ICollection<EmploymentStatusHistory> historyEntries)
    {
        if (correction.Status != ApprovalStatus.Pending)
            return (false, "طلب التصحيح ليس قيد الانتظار.");

        if (string.IsNullOrWhiteSpace(correction.Reason))
            return (false, "سبب التصحيح مطلوب.");

        using var doc = JsonDocument.Parse(correction.ChangesJson);
        if (!doc.RootElement.TryGetProperty("after", out var afterEl))
            return (false, "صيغة ChangesJson غير صالحة.");

        foreach (var prop in afterEl.EnumerateObject())
        {
            if (string.Equals(prop.Name, nameof(Employee.CurrentStatus), StringComparison.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse<EmploymentStatus>(prop.Value.GetString(), ignoreCase: true, out var status))
                    return (false, "حالة وظيفية غير صالحة.");

                var (ok, err) = EmployeeLifecycleService.TryChangeStatus(
                    employee, status, correction.Reason, approvedBy, historyEntries, allowAdminOverride: true);
                if (!ok) return (false, err);
                continue;
            }

            ApplyField(employee, prop.Name, prop.Value);
        }

        if (!string.IsNullOrWhiteSpace(employee.FirstName) || !string.IsNullOrWhiteSpace(employee.LastName))
        {
            employee.FullName = string.Join(" ", new[]
            {
                employee.FirstName,
                employee.MiddleName,
                employee.LastName
            }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        correction.Status = ApprovalStatus.Approved;
        correction.ApprovedBy = approvedBy;
        correction.ApprovedAt = DateTime.UtcNow;
        return (true, null);
    }

    private static string CanonicalField(string key) => key switch
    {
        "DateOfBirth" => nameof(Employee.BirthDate),
        "EmploymentDate" => nameof(Employee.HireDate),
        _ => key
    };

    private static object? ReadCurrent(Employee e, string field) => field switch
    {
        nameof(Employee.NationalId) => e.NationalId,
        nameof(Employee.FirstName) => e.FirstName,
        nameof(Employee.MiddleName) => e.MiddleName,
        nameof(Employee.LastName) => e.LastName,
        nameof(Employee.FullName) => e.FullName,
        nameof(Employee.BirthDate) => e.BirthDate,
        nameof(Employee.HireDate) => e.HireDate,
        nameof(Employee.Gender) => e.Gender.ToString(),
        nameof(Employee.MaritalStatus) => e.MaritalStatus.ToString(),
        nameof(Employee.CurrentStatus) => e.CurrentStatus.ToString(),
        _ => null
    };

    private static void ApplyField(Employee e, string field, JsonElement value)
    {
        switch (CanonicalField(field))
        {
            case nameof(Employee.NationalId):
                e.NationalId = value.GetString() ?? e.NationalId;
                break;
            case nameof(Employee.FirstName):
                e.FirstName = value.GetString() ?? e.FirstName;
                break;
            case nameof(Employee.MiddleName):
                e.MiddleName = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
                break;
            case nameof(Employee.LastName):
                e.LastName = value.GetString() ?? e.LastName;
                break;
            case nameof(Employee.FullName):
                e.FullName = value.GetString() ?? e.FullName;
                break;
            case nameof(Employee.BirthDate):
                e.BirthDate = value.GetDateTime();
                break;
            case nameof(Employee.HireDate):
                e.HireDate = value.ValueKind == JsonValueKind.Null ? null : value.GetDateTime();
                break;
            case nameof(Employee.Gender) when Enum.TryParse<Gender>(value.GetString(), true, out var g):
                e.Gender = g;
                break;
            case nameof(Employee.MaritalStatus) when Enum.TryParse<MaritalStatus>(value.GetString(), true, out var m):
                e.MaritalStatus = m;
                break;
        }
    }

    private static object? JsonElementToClr(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var l) => l,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };
}
