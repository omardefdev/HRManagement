using Microsoft.Extensions.Caching.Memory;

namespace HRManagement.ServiceDefaults;

public interface IGatewayProofReplayCache
{
    /// <summary>True the first time a nonce is seen in this instance within <paramref name="lifetime"/>; false for a replay.</summary>
    bool TryRegister(string nonce, TimeSpan lifetime);
}

/// <summary>
/// PLAN-42: the nonces of proofs this instance accepted, for twice the allowed skew. It is local: a replay to a second instance
/// inside the skew window is an accepted risk (ADR-S2S-042), and the proof only works with the token it was made for. Only a
/// proof with a valid signature reaches this cache, so a caller without the gateway's key cannot fill it.
/// </summary>
public sealed class GatewayProofReplayCache(IMemoryCache cache) : IGatewayProofReplayCache
{
    private const string KeyPrefix = "gateway-proof-nonce:";
    private readonly object _gate = new();

    public bool TryRegister(string nonce, TimeSpan lifetime)
    {
        var key = KeyPrefix + nonce;
        lock (_gate)
        {
            if (cache.TryGetValue(key, out _))
                return false;
            cache.Set(key, true, lifetime);
            return true;
        }
    }
}
