namespace HR.Contracts.Application.Abstractions;

public interface IExecutionContextAccessor
{
    string? CorrelationId { get; }
    string SourceSystem { get; }
}
