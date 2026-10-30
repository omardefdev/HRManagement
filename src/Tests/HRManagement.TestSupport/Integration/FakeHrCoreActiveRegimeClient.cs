using HRManagement.SharedKernel.Legal;

namespace HRManagement.TestSupport.Integration;

/// <summary>
/// QA-034: declared stand-in for the HR.Core active-regime S2S client in integration hosts (HR.Contracts separation
/// publish, HR.Movements approval). The default returns <c>law50</c> (docs/syr/service-regimes.json) for every requested
/// employee. <see cref="FakeRegimeMode.NoRegime"/> returns an empty list (the caller must refuse with its "regime rules
/// not found" message); <see cref="FakeRegimeMode.Unavailable"/> throws like an unreachable HR.Core.
/// </summary>
public sealed class FakeHrCoreActiveRegimeClient(
    FakeRegimeMode mode = FakeRegimeMode.Law50,
    string legalRegimeId = FakeHrCoreActiveRegimeClient.Law50) : IHrCoreActiveRegimeClient
{
    public const string Law50 = "law50";

    public FakeRegimeMode Mode { get; set; } = mode;

    public List<IReadOnlyList<Guid>> Requests { get; } = [];

    public Task<IReadOnlyList<HrCoreActiveRegimeDto>> GetActiveRegimesAsync(
        IReadOnlyList<Guid> employeeIds,
        CancellationToken ct = default)
    {
        Requests.Add(employeeIds);
        return Mode switch
        {
            FakeRegimeMode.NoRegime => Task.FromResult<IReadOnlyList<HrCoreActiveRegimeDto>>([]),
            FakeRegimeMode.Unavailable => throw new HttpRequestException("HR.Core active-regime endpoint unavailable (fake)"),
            _ => Task.FromResult<IReadOnlyList<HrCoreActiveRegimeDto>>(
                employeeIds.Select(id => new HrCoreActiveRegimeDto(id, legalRegimeId, null)).ToList())
        };
    }
}

public enum FakeRegimeMode
{
    Law50,
    NoRegime,
    Unavailable
}
