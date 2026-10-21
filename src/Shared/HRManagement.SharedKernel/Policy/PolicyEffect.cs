namespace HRManagement.SharedKernel.Policy;

public enum PolicyEffect
{
    Allow,
    Deny,
    RequireEvidence,
    RequireApproval,
    RequireDualApproval,
    TriggerWorkflow,
    SetField,
    RequireOriginReceipt,
    RequirePrivacyMask,
    BlockUntilChecklist,
    SelectCodePolicy
}
