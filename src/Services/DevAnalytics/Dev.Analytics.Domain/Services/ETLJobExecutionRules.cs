using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Enums;

namespace Dev.Analytics.Domain.Services;

public static class ETLJobExecutionRules
{
    public static bool CanRun(JobStatus status) =>
        status == JobStatus.Ok;

    public static bool ShouldRefresh(DataRefreshSchedule schedule) =>
        schedule.NextRunAt.HasValue && schedule.NextRunAt.Value <= DateTime.UtcNow;

    public static bool IsStale(ReadModel model, int maxHours) =>
        model.LastRefreshed.HasValue &&
        model.LastRefreshed.Value.AddHours(maxHours) < DateTime.UtcNow;
}
