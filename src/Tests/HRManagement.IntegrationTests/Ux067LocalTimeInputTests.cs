using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults.Localization;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-67 (D-67-2, P-7): a <c>datetime-local</c> input bound with <c>asp-for</c> shows the stored UTC value on the Damascus clock
/// (the shared tag helper), with no change to the view. The delegation form fills <c>ActiveFrom</c> with the current UTC time; the
/// page must show it three hours later, as the user's clock reads.
/// </summary>
[Collection(SharedHrCoreHost.Name)]
public sealed class Ux067LocalTimeInputTests(Hcm025ScopeAndPolicyTestHost host)
{
    private static readonly Regex ActiveFrom = new("<input[^>]*name=\"ActiveFrom\"[^>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex Value = new("value=\"(?<v>[^\"]*)\"", RegexOptions.CultureInvariant);

    [Fact]
    public async Task A_datetime_local_input_shows_the_stored_time_on_the_damascus_clock()
    {
        using var client = host.Client(Guid.NewGuid(), "hr-admin");
        var before = DateTime.UtcNow;
        var response = await client.GetAsync("/Delegations/Create");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadAsStringAsync();

        var input = ActiveFrom.Match(page);
        Assert.True(input.Success, "The delegation form has no ActiveFrom input.");
        Assert.Contains("type=\"datetime-local\"", input.Value, StringComparison.Ordinal);
        var shown = DateTime.ParseExact(Value.Match(input.Value).Groups["v"].Value, HrLocalTime.DateTimeLocalInputFormat, CultureInfo.InvariantCulture);

        var expected = HrLocalTime.ToLocal(before, HrLocalTime.DefaultZone);
        Assert.InRange(shown, expected.AddMinutes(-1), expected.AddMinutes(2));
        Assert.True(shown - before > TimeSpan.FromHours(2.9), "The input shows the stored UTC value, not the Damascus clock.");
    }
}

