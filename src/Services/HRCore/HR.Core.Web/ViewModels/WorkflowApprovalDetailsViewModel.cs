using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Web.ViewModels;

/// <summary>PLAN-45: what the generic approval page shows. PLAN-46 reuses it for its types.</summary>
public sealed class WorkflowApprovalDetailsViewModel
{
    /// <summary>Labels of the facts the owning services send. Any other key is shown as it came.</summary>
    public static readonly IReadOnlyDictionary<string, string> FactLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["assetId"] = "معرّف الأصل",
        ["tagNumber"] = "رقم العهدة",
        ["isPrivileged"] = "أصل مميز",
        ["value"] = "القيمة",
        ["employeeId"] = "معرّف الموظف",
        ["lossReportId"] = "معرّف تقرير الفقدان",
        ["reason"] = "السبب",
        ["cardPurpose"] = "الغرض من البطاقة",
        ["employeeName"] = "الموظف",
        ["actionType"] = "نوع الإجراء",
        ["issuedAt"] = "تاريخ الإجراء",
        ["complaintId"] = "معرّف الشكوى"
    };

    public Guid InstanceId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string WorkflowName { get; init; } = string.Empty;

    public WorkflowStatus Status { get; init; }

    public string? RequesterName { get; init; }

    public string? SubjectName { get; init; }

    public DateTime StartedAt { get; init; }

    public DateTime? CompletedAt { get; init; }

    public string? DetailsUrl { get; init; }

    public IReadOnlyDictionary<string, string> Facts { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<WorkflowStepHistoryEntry> History { get; init; } = [];

    public WorkflowPanelViewModel Panel { get; init; } = new();

    public static string FactLabel(string key) => FactLabels.TryGetValue(key, out var label) ? label : key;
}
