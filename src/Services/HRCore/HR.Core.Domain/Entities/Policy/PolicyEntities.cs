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

    public ICollection<PolicyRule> Rules { get; set; } = [];
    public ICollection<PolicyPublication> Publications { get; set; } = [];
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

    public string? ShadowDiffJson { get; set; }

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
