namespace HRManagement.SharedKernel.SeparationOfDuties;

/// <summary>Lifecycle action subject to Separation of Duties (IAM-003).</summary>
public enum SodAction
{
    Make,
    Check,
    Post,
    Reverse
}
