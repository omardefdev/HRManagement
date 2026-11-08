using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities.Policy;

public enum PolicyTemplateScope
{
    National,
    TenantOverride
}

public enum PolicyVersionStatus
{
    Draft,
    Published,
    Archived
}

[Display(Name = "قالب سياسة")]
public class PolicyTemplate : AuditableEntity
{
    [Required, MaxLength(100)]
    public string Domain { get; set; } = default!;

    [Required, MaxLength(200)]
    public string Name { get; set; } = default!;

    public PolicyTemplateScope Scope { get; set; } = PolicyTemplateScope.National;

    [MaxLength(100)]
    public string SubjectType { get; set; } = default!;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public ICollection<PolicyTemplateVersion> Versions { get; set; } = [];
}

[Display(Name = "إصدار سياسة")]
public class PolicyTemplateVersion : AuditableEntity
{
    public Guid PolicyTemplateId { get; set; }
    public PolicyTemplate PolicyTemplate { get; set; } = default!;

    public int VersionNumber { get; set; } = 1;
    public PolicyVersionStatus Status { get; set; } = PolicyVersionStatus.Draft;

    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }

    /// <summary>PLAN-47: a rollback draft: the earlier version it copies. Published as <see cref="PolicyPublicationKind.Rollback"/>.</summary>
    public Guid? RollbackOfVersionId { get; set; }

    /// <summary>PLAN-47: the written reason of a rollback, or of a draft that withdraws every rule of the domain.</summary>
    [MaxLength(1000)]
    public string? ChangeReason { get; set; }

    /// <summary>PLAN-47: the explicit "withdraw every rule of the domain" draft: the only version that may publish with no rule.</summary>
    public bool WithdrawsAllRules { get; set; }

    public ICollection<PolicyRule> Rules { get; set; } = [];
    public ICollection<PolicyPublication> Publications { get; set; } = [];
}

/// <summary>PLAN-47: how a version came to be published.</summary>
public enum PolicyPublicationKind
{
    Manual,
    SystemSeed,
    Rollback
}

/// <summary>PLAN-47: what was done to a draft.</summary>
public enum PolicyVersionEditAction
{
    Create,
    Copy,
    AddRule,
    UpdateRule,
    RemoveRule,
    SetEffectiveFrom,
    Discard
}

/// <summary>
/// PLAN-47 (G-POL-06): every change to a draft, by whom. The publisher must not be among a version's editors (four eyes).
/// </summary>
[Display(Name = "تعديل مسودة سياسة")]
public class PolicyVersionEdit : BaseEntity
{
    public Guid PolicyTemplateVersionId { get; set; }

    /// <summary>The user who made the change; null for the system (a seed change set waiting for an approver).</summary>
    public Guid? UserId { get; set; }

    public PolicyVersionEditAction Action { get; set; }

    [MaxLength(100)]
    public string? RuleKey { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// PLAN-47 (§4): one seed change set applied to one template. Never changed: an applied set whose digest differs fails the boot.
/// </summary>
[Display(Name = "تطبيق مجموعة تغيير بذور السياسة")]
public class PolicySeedApplication : BaseEntity
{
    [Required, MaxLength(100)]
    public string ChangeSetId { get; set; } = default!;

    public Guid PolicyTemplateId { get; set; }

    /// <summary>The version the set published (or left as a draft); null for a NoOp.</summary>
    public Guid? ResultVersionId { get; set; }

    [Required, MaxLength(20)]
    public string Outcome { get; set; } = default!;

    /// <summary>SHA-256 (hex) of the set's operations.</summary>
    [Required, MaxLength(64)]
    public string Digest { get; set; } = default!;

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
}

[Display(Name = "قاعدة سياسة")]
public class PolicyRule : BaseEntity
{
    public Guid PolicyTemplateVersionId { get; set; }
    public PolicyTemplateVersion PolicyTemplateVersion { get; set; } = default!;

    [MaxLength(100)]
    public string? RuleKey { get; set; }

    [MaxLength(200)]
    public string? Name { get; set; }

    public int Priority { get; set; } = 100;

    /// <summary>JSON PolicyConditionGroup.</summary>
    public string WhenJson { get; set; } = "{}";

    /// <summary>JSON array of PolicyEffectDefinition.</summary>
    public string ThenJson { get; set; } = "[]";
}

[Display(Name = "مخطط حقائق السياسة")]
public class PolicyFactsSchemaEntity : AuditableEntity
{
    [Required, MaxLength(100)]
    public string Domain { get; set; } = default!;

    [Required, MaxLength(100)]
    public string SubjectType { get; set; } = default!;

    public string FieldsJson { get; set; } = "[]";
}

[Display(Name = "نشر سياسة")]
public class PolicyPublication : BaseEntity
{
    public Guid PolicyTemplateVersionId { get; set; }
    public PolicyTemplateVersion PolicyTemplateVersion { get; set; } = default!;

    public Guid? MakerUserId { get; set; }
    public Guid? CheckerUserId { get; set; }

    [MaxLength(256)]
    public string? EvidenceRef { get; set; }

    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    public Guid? RolledBackFromVersionId { get; set; }

    /// <summary>PLAN-47: Manual, SystemSeed (a seed change set, D-POL-11) or Rollback.</summary>
    public PolicyPublicationKind Kind { get; set; } = PolicyPublicationKind.Manual;

    /// <summary>PLAN-47: the role the maker acted in (policy-author, or system for a seed).</summary>
    [MaxLength(100)]
    public string? MakerRole { get; set; }

    /// <summary>PLAN-47: the role the checker acted in (policy-approver).</summary>
    [MaxLength(100)]
    public string? CheckerRole { get; set; }
}

[Display(Name = "محاكاة سياسة")]
public class PolicySimulationRun : BaseEntity
{
    public Guid PolicyTemplateVersionId { get; set; }
    public string FactsJson { get; set; } = "{}";
    public string DecisionJson { get; set; } = "{}";
    public Guid? RunByUserId { get; set; }
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
}

[Display(Name = "سجل تطبيق السياسة")]
public class PolicyEnforcementLedger : BaseEntity
{
    [MaxLength(100)]
    public string Domain { get; set; } = default!;

    [MaxLength(64)]
    public string Action { get; set; } = default!;

    public Guid? PolicyVersionId { get; set; }

    [MaxLength(128)]
    public string? CorrelationId { get; set; }

    [MaxLength(64)]
    public string FactsHash { get; set; } = default!;

    public string DecisionJson { get; set; } = "{}";

    public Guid? ActorUserId { get; set; }

    [MaxLength(200)]
    public string? ActorUserName { get; set; }

    public PolicyEnforcementModeValue Mode { get; set; }

    public Guid? WorkflowInstanceId { get; set; }

    /// <summary>PLAN-42: the service client that asked; null for a decision taken inside HR.Core.</summary>
    [MaxLength(100)]
    public string? CallerClientId { get; set; }

    /// <summary>PLAN-42: what was decided on.</summary>
    [MaxLength(100)]
    public string? SubjectType { get; set; }

    public Guid? EntityId { get; set; }

    public string? ShadowDiffJson { get; set; }

    /// <summary>PLAN-47: the "as of" time of the decision, when it was not the time of the request.</summary>
    public DateTime? EvaluatedAsOf { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public enum PolicyEnforcementModeValue
{
    Off,
    Shadow,
    Enforce
}

[Display(Name = "ربط workflow بالسياسة")]
public class PolicyWorkflowBinding : AuditableEntity
{
    [MaxLength(100)]
    public string Domain { get; set; } = default!;

    [MaxLength(50)]
    public string WorkflowCode { get; set; } = default!;

    public Guid? WorkflowDefinitionId { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }
}
