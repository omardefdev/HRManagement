using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities.CodeGeneration;

public enum CodePolicyScope
{
    National,
    TenantOverride
}

public enum CodePolicyVersionStatus
{
    Draft,
    Published,
    Archived
}

[Display(Name = "قالب ترميز")]
public class CodePolicyTemplate : AuditableEntity
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = default!;

    public CodeEntityKind EntityKind { get; set; }

    public CodePolicyScope Scope { get; set; } = CodePolicyScope.National;

    [MaxLength(100)]
    public string? PolicyKey { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public ICollection<CodePolicyVersion> Versions { get; set; } = [];
    public ICollection<CodePolicyRule> Rules { get; set; } = [];
}

[Display(Name = "إصدار سياسة ترميز")]
public class CodePolicyVersion : AuditableEntity
{
    public Guid CodePolicyTemplateId { get; set; }
    public CodePolicyTemplate Template { get; set; } = default!;

    public int VersionNumber { get; set; } = 1;
    public CodePolicyVersionStatus Status { get; set; } = CodePolicyVersionStatus.Draft;

    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }

    public string SegmentsJson { get; set; } = "[]";

    public CodeSequenceReset SequenceReset { get; set; } = CodeSequenceReset.Yearly;

    [MaxLength(20)]
    public string FallbackPrefix { get; set; } = "GEN";
}

[Display(Name = "قاعدة سياسة ترميز")]
public class CodePolicyRule : BaseEntity
{
    public Guid CodePolicyTemplateId { get; set; }
    public CodePolicyTemplate Template { get; set; } = default!;

    public Guid CodePolicyVersionId { get; set; }
    public CodePolicyVersion Version { get; set; } = default!;

    [MaxLength(100)]
    public string? RuleKey { get; set; }

    [MaxLength(200)]
    public string? Name { get; set; }

    public int Priority { get; set; } = 100;

    public string WhenJson { get; set; } = "{}";
}

[Display(Name = "عداد تسلسل")]
public class CodeSequenceCounter : BaseEntity
{
    public CodeEntityKind EntityKind { get; set; }

    [MaxLength(256)]
    public string ScopeKey { get; set; } = string.Empty;

    public long LastValue { get; set; }

    public int Year { get; set; }
}

[Display(Name = "سجل إصدار رمز")]
public class CodeIssuance : BaseEntity
{
    public CodeEntityKind EntityKind { get; set; }

    [MaxLength(128)]
    public string Code { get; set; } = default!;

    public Guid? PolicyVersionId { get; set; }

    [MaxLength(256)]
    public string SequenceScope { get; set; } = string.Empty;

    public string FactsJson { get; set; } = "{}";

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
}
