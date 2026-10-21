namespace HR.Core.Infrastructure.Services.Establishment;

public sealed class JobCatalogPayload
{
    public Guid? Id { get; set; }
    public string JobCode { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public string? TitleEn { get; set; }
    public string? DescriptionAr { get; set; }
    public string GradeCode { get; set; } = string.Empty;
    public string? OccupationalGroup { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class JobCatalogBulkPayload
{
    public Guid ImportBatchId { get; set; }
    public IReadOnlyList<JobCatalogPayload> Items { get; set; } = [];
}

public sealed class FundedPositionCreatePayload
{
    public int BudgetYear { get; set; }
    public Guid OrgUnitId { get; set; }
    public Guid JobCatalogEntryId { get; set; }
    public string GradeCode { get; set; } = string.Empty;
    public decimal FTE { get; set; } = 1.0m;
}

public sealed class FreezeFundedPositionPayload
{
    public Guid FundedPositionId { get; set; }
    public string FreezeReason { get; set; } = string.Empty;
}

public sealed class UnfreezeFundedPositionPayload
{
    public Guid FundedPositionId { get; set; }
}
