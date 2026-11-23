using Payroll.Application.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-53: records the salary change events a unit of work staged, in order, instead of the bus outbox.</summary>
public sealed class RecordingPayProfileChangeEvents : IPayProfileChangeEventPublisher
{
    public List<object> Published { get; } = [];

    /// <summary>When set, publishing the applied event throws: a failure inside the applier after the version was created.</summary>
    public bool FailOnApplied { get; set; }

    public Task PublishSubmittedAsync(PayProfileChangeSubmittedEvent message, CancellationToken ct)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public Task PublishDecidedAsync(PayProfileChangeDecidedEvent message, CancellationToken ct)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public Task PublishAppliedAsync(PayProfileChangeAppliedEvent message, CancellationToken ct)
    {
        if (FailOnApplied)
            throw new InvalidOperationException("planted failure after the new version was created");
        Published.Add(message);
        return Task.CompletedTask;
    }
}
