namespace Compliance.Domain.Services;

/// <summary>TRN-002: wave cannot start without engagement evidence or signed exemption.</summary>
public static class UnionEngagementGate
{
    public static bool CanStartWave(
        bool hasEngagementRecord,
        bool hasSignedExemption,
        IEnumerable<string> openObjectionStatuses)
    {
        if (!hasEngagementRecord && !hasSignedExemption)
            return false;

        // Unresolved objections block the wave.
        if (openObjectionStatuses.Any(s =>
                string.Equals(s, "Pending", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "Escalated", StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    public static void EnsureCollectiveNotRoutedToIndividual(bool routedToIndividual)
    {
        if (routedToIndividual)
            throw new InvalidOperationException(
                "التظلم الجماعي لا يُدفع إلى المسار الفردي (TRN-002).");
    }
}
