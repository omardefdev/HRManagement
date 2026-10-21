using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;

namespace HRManagement.UnitTests;

public class GatewayJourneyCatalogTests
{
    private readonly GatewayJourneyCatalog _catalog = new();

    [Fact]
    public void NewHire_has_recruitment_and_onboarding_phases()
    {
        var journey = _catalog.FindById("new-hire");
        Assert.NotNull(journey);

        var phaseKeys = journey.Steps.Select(s => s.PhaseKey).Distinct().ToList();
        Assert.Contains("recruitment", phaseKeys);
        Assert.Contains("onboarding", phaseKeys);
    }

    [Fact]
    public void NewHire_has_at_least_twelve_recruitment_steps_and_nine_onboarding_steps()
    {
        var journey = _catalog.FindById("new-hire");
        Assert.NotNull(journey);

        var recruitment = journey.Steps.Count(s => s.PhaseKey == "recruitment");
        var onboarding = journey.Steps.Count(s => s.PhaseKey == "onboarding");

        Assert.True(recruitment >= 12, $"Expected >= 12 recruitment steps, got {recruitment}");
        Assert.True(onboarding >= 9, $"Expected >= 9 onboarding steps, got {onboarding}");
    }

    [Fact]
    public void NewHire_includes_careers_public_step()
    {
        var journey = _catalog.FindById("new-hire");
        Assert.NotNull(journey);

        Assert.Contains(journey.Steps, s => s.GatewayPublicPath == "/careers");
    }

    [Fact]
    public void NewHire_internal_channel_steps_are_optional()
    {
        var journey = _catalog.FindById("new-hire");
        Assert.NotNull(journey);

        var talent = journey.Steps.Single(s => s.UiPath == "/ui/hr-core/TalentMarketplace");
        var ess = journey.Steps.Single(s => s.UiPath == "/ui/hr-core/EssTalent");

        Assert.False(talent.IsRequired);
        Assert.False(ess.IsRequired);
    }

    [Fact]
    public void NewHire_module_paths_are_ui_or_public_allowlist()
    {
        var journey = _catalog.FindById("new-hire");
        Assert.NotNull(journey);

        foreach (var step in journey.Steps)
        {
            if (!string.IsNullOrWhiteSpace(step.GatewayPublicPath))
            {
                Assert.True(
                    GatewayJourneyPublicPaths.TryResolve(step.GatewayPublicPath, out _),
                    $"Unexpected public path: {step.GatewayPublicPath}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(step.UiPath))
                continue;

            Assert.StartsWith("/ui/", step.UiPath);
        }
    }

    [Fact]
    public void NewHire_phase_titles_mark_recruitment_and_onboarding()
    {
        var journey = _catalog.FindById("new-hire");
        Assert.NotNull(journey);

        Assert.Contains(journey.Steps, s =>
            s.PhaseKey == "recruitment" && s.PhaseTitleAr?.Contains("HCM-001") == true);
        Assert.Contains(journey.Steps, s =>
            s.PhaseKey == "onboarding" && s.PhaseTitleAr?.Contains("FUN-001") == true);
    }
}
