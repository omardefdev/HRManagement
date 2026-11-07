namespace Admin.Logistics.Infrastructure.Services;

/// <summary>PLAN-45: <c>AssetApproval</c>.</summary>
public sealed class AssetApprovalOptions
{
    public const string SectionName = "AssetApproval";

    public RetryOptions Retry { get; set; } = new();

    public sealed class RetryOptions
    {
        public int IntervalSeconds { get; set; } = 120;

        public int MaxAttempts { get; set; } = 30;
    }
}
