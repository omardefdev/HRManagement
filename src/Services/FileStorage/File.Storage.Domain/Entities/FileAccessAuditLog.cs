using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace File.Storage.Domain.Entities;

[Display(Name = "سجل الوصول للملف")]
public class FileAccessAuditLog : BaseEntity
{
    public Guid FileId { get; set; }

    [StringLength(64)]
    public string Action { get; set; } = default!;

    public Guid? UserId { get; set; }

    [StringLength(256)]
    public string? UserName { get; set; }

    [StringLength(64)]
    public string? ClientIp { get; set; }

    /// <summary>SEC-003: whether the object-level policy allowed the action.</summary>
    public bool? DecisionAllowed { get; set; }

    /// <summary>SEC-003: policy decision reason (grant/deny).</summary>
    [StringLength(256)]
    public string? DecisionReason { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
