namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>Outcome of an SoD validation check (IAM-003).</summary>
public record SodResult(bool Allowed, string? DenialReason)
{
    public static SodResult Ok() => new(true, null);

    public static SodResult Deny(string reason) => new(false, reason);
}
