namespace HRManagement.ServiceDefaults.Policy;

/// <summary>
/// PLAN-49 (the second review of P5): HR.Core's policy engine could not be asked while the domain runs in Enforce, where an
/// unanswered question fails closed. For a consumer that is an outage, not a decision. A transient fault: it derives directly
/// from <see cref="Exception"/> (the CodeGenerationUnavailableException pattern), not from
/// <see cref="InvalidOperationException"/> or <see cref="ArgumentException"/>, which the retry policy of every bus ignores
/// (MassTransitExtensions.ConfigureMessageRetry). So the message is retried, and when HR.Core stays away it moves to its
/// <c>_error</c> queue, from which PLAN-40's replay brings it back: nothing is recorded as decided meanwhile.
/// The domain and the action are the calling service's own constants, never text of a request.
/// </summary>
public sealed class PolicyEngineUnavailableException(string domain, string action)
    : Exception($"The policy engine could not be asked about {domain}/{action}: the message is retried, not decided.")
{
    public string Domain { get; } = domain;

    public string Action { get; } = action;
}
