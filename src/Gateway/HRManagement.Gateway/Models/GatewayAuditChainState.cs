namespace HRManagement.Gateway.Models;

/// <summary>Cluster-wide monotonic sequence allocator per audit hash chain (HA-DR).</summary>
public sealed class GatewayAuditChainState
{
    public string ChainKey { get; set; } = "default";
    public long LastSequence { get; set; }
}
