using System.ComponentModel.DataAnnotations;

namespace HRManagement.SharedKernel.Exports;

public enum ExportJobStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending = 0,
    [Display(Name = "قيد التنفيذ")]
    Running = 1,
    [Display(Name = "مكتمل")]
    Completed = 2,
    [Display(Name = "فشل")]
    Failed = 3,
    [Display(Name = "ملغى")]
    Cancelled = 4
}

/// <summary>PERF-004: authorization snapshot captured at enqueue; re-checked on download.</summary>
public sealed class ExportAuthSnapshot
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ScopeOrgUnitId { get; set; }
    public string? ScopeIdsHash { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}

public sealed class ExportJobOptions
{
    public const string SectionName = "ExportJobs";

    public int ChunkSize { get; set; } = 500;
    public int ResultRetentionHours { get; set; } = 24;
    public int DownloadPresignMinutes { get; set; } = 15;
    public int LeaseSeconds { get; set; } = 120;
    public string ArtifactBucket { get; set; } = "exports";
}

public static class ExportJobTemplates
{
    public const string PayrollRunCsv = "payroll.run.csv";
    public const string PayrollBulkPayslips = "payroll.bulk.payslips";
    public const string PayrollRegister = "payroll.register.csv";
    public const string PayrollEosCsv = "payroll.eos.csv";
    public const string HrCoreEmployeesCsv = "hrcore.employees.csv";
    public const string ContractsLeaveCsv = "contracts.leave.csv";
    public const string ContractsAdvanceCsv = "contracts.advance.csv";
    public const string ComplianceComplaintsCsv = "compliance.complaints.csv";
    public const string AnalyticsWorkforceCsv = "analytics.workforce.csv";
    public const string ArchiveInventoryCsv = "archive.inventory.csv";
    public const string Version1 = "1";
}

public sealed class ExportJobStatusDto
{
    public Guid Id { get; set; }
    public string ExportType { get; set; } = "";
    public ExportJobStatus Status { get; set; }
    public int TotalCount { get; set; }
    public int ProcessedCount { get; set; }
    public string? Error { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool HasResult { get; set; }
    public string TemplateCode { get; set; } = "";
    public string TemplateVersion { get; set; } = "";
}
