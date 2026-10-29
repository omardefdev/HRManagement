using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;

namespace HR.Core.Web.ViewModels;

/// <summary>HCM-031 (PLAN-31): marker screens — exam numbers and marks only, never an applicant field.</summary>
public sealed class ExamMarkingIndexViewModel
{
    public IReadOnlyList<MarkerSessionView> Sessions { get; init; } = [];
    public bool MissingEmployee { get; init; }
    public bool HoldsHrReadAccess { get; init; }
}

public sealed class ExamMarkingSessionViewModel
{
    public Guid SessionId { get; init; }
    public string SessionTitle { get; init; } = string.Empty;
    public ExamMarkerRole Role { get; init; }
    public bool Locked { get; init; }
    public IReadOnlyList<MarkerPaperView> Papers { get; init; } = [];
}

public sealed class ExamMarkingPaperViewModel
{
    public Guid SessionId { get; init; }
    public ExamMarkerRole Role { get; init; }
    public required MarkerPaperView Paper { get; init; }
}
