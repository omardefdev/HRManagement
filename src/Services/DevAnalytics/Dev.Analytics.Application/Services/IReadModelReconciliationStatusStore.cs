using Dev.Analytics.Application.Dtos;

namespace Dev.Analytics.Application.Services;

public interface IReadModelReconciliationStatusStore
{
    void Save(ReadModelReconciliationReport report);

    ReadModelReconciliationReport? GetLast(Guid readModelId);
}

public sealed class InMemoryReadModelReconciliationStatusStore : IReadModelReconciliationStatusStore
{
    private readonly Dictionary<Guid, ReadModelReconciliationReport> _last = [];

    public void Save(ReadModelReconciliationReport report) =>
        _last[report.ReadModelId] = report;

    public ReadModelReconciliationReport? GetLast(Guid readModelId) =>
        _last.TryGetValue(readModelId, out var report) ? report : null;
}
