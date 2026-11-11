using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Web.ViewModels;

public class WorkflowPanelViewModel
{
    public bool HasWorkflowInstance { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public WorkflowStatus? WorkflowStatus { get; set; }
    public string? WorkflowCode { get; set; }
    public string? WorkflowName { get; set; }
    public int? CurrentStepLevel { get; set; }
    public string? CurrentStepName { get; set; }
    public string? CurrentStepRoleRequired { get; set; }
    public int? ApproveTargetStepLevel { get; set; }
    public bool CanApprove { get; set; }
    public bool CanReject { get; set; }
    public string? BlockReason { get; set; }
    public string? MissingWorkflowWarning { get; set; }
    /// <summary>PLAN-25: the stuck item can be given its workflow from the panel (HrCoreWrite).</summary>
    public bool CanRestartApproval { get; set; }
    /// <summary>PLAN-50 (§6): the delegation through which the user may act on this step (he does not hold its role); null when direct.</summary>
    public HRManagement.SharedKernel.SeparationOfDuties.ActiveDelegation? OnBehalfOf { get; set; }
    /// <summary>«بالنيابة عن {الأصيل}».</summary>
    public string? OnBehalfOfName => OnBehalfOf is null ? null : OnBehalfOf.PrincipalName ?? OnBehalfOf.PrincipalUserId.ToString("D");
    public IReadOnlyList<WorkflowStepViewModel> Steps { get; set; } = [];
}

public class WorkflowStepViewModel
{
    public int Level { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RoleRequired { get; set; } = string.Empty;
    /// <summary>completed | current | upcoming</summary>
    public string State { get; set; } = "upcoming";
}

public class WorkflowActionFormViewModel
{
    public Guid EntityId { get; set; }

    [System.ComponentModel.DataAnnotations.Display(Name = "ملاحظات")]
    [System.ComponentModel.DataAnnotations.StringLength(500)]
    public string? Comments { get; set; }
}
