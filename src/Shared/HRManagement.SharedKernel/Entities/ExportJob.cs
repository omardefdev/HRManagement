using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Exports;

namespace HRManagement.SharedKernel.Entities;

/// <summary>PERF-004: durable background export job (per-service table mapping).</summary>
public class ExportJob : AuditableEntity
{
    [MaxLength(64)]
    public string ServiceKey { get; set; } = "";

    [MaxLength(128)]
    public string ExportType { get; set; } = "";

    public ExportJobStatus Status { get; set; } = ExportJobStatus.Pending;

    public Guid RequestedByUserId { get; set; }

    public string FilterJson { get; set; } = "{}";

    public string AuthSnapshotJson { get; set; } = "{}";

    [MaxLength(128)]
    public string TemplateCode { get; set; } = "";

    [MaxLength(32)]
    public string TemplateVersion { get; set; } = ExportJobTemplates.Version1;

    public int TotalCount { get; set; }

    public int ProcessedCount { get; set; }

    [MaxLength(100)]
    public string? LeaseOwner { get; set; }

    public DateTime? LeaseUntil { get; set; }

    [MaxLength(2000)]
    public string? Error { get; set; }

    public Guid? ResultFileId { get; set; }

    [MaxLength(128)]
    public string? ResultBucket { get; set; }

    [MaxLength(512)]
    public string? ResultObjectKey { get; set; }

    [MaxLength(128)]
    public string? ResultContentType { get; set; }

    [MaxLength(256)]
    public string? ResultFileName { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
