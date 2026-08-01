using HRManagement.SharedKernel.Evidence;

namespace HRManagement.ServiceDefaults.Evidence;

public interface IEvidenceCollector
{
    Task<EvidenceRecord> RecordAsync(EvidenceWriteRequest request, CancellationToken cancellationToken = default);
}

public sealed record EvidenceWriteRequest(
    Guid TenantId,
    string CorrelationId,
    string Actor,
    string Resource,
    string Purpose,
    string Result,
    string? Subject = null,
    string? CausationId = null,
    string? AssuranceLevel = null,
    string? Policy = null,
    string? Rule = null,
    string? Reason = null,
    string? BeforeHash = null,
    string? AfterHash = null,
    string? EvidenceLinksJson = null,
    string? ReleaseVersion = null,
    string? ConfigVersion = null,
    string TimeSource = "system-clock",
    string TimeZoneId = "UTC",
    string ChainKey = EvidenceHashChain.DefaultChainKey);
