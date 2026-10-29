namespace HR.Core.Web.ViewModels;

/// <summary>
/// HCM-031 (PLAN-31, P2): public apply form of a competition — the PLAN-30 apply fields plus ordered line preferences
/// (slot 1 = first choice, up to <c>MaxLinesPerCandidate</c>).
/// </summary>
public sealed class CompetitionApplyViewModel : CitizenApplicationApplyViewModel
{
    public Guid CompetitionId { get; set; }

    public List<Guid?> Preferences { get; set; } = [];
}
