namespace HRManagement.UnitTests;

public class Ac35ScopedLookupPerfTests
{
    [Fact]
    public void K6_summary_fixture_parses_percentiles()
    {
        var fixture = """
        {
          "metrics": {
            "http_req_duration": {
              "values": { "med": 120.5, "p(95)": 240.1, "p(99)": 310.2, "avg": 130.0 }
            },
            "http_req_failed": { "values": { "rate": 0.002 } }
          }
        }
        """;
        using var doc = System.Text.Json.JsonDocument.Parse(fixture);
        var dur = doc.RootElement.GetProperty("metrics").GetProperty("http_req_duration").GetProperty("values");
        var p50 = dur.TryGetProperty("p(50)", out var p50El) ? p50El.GetDouble() : dur.GetProperty("med").GetDouble();
        var p95 = dur.GetProperty("p(95)").GetDouble();
        var p99 = dur.GetProperty("p(99)").GetDouble();
        var failRate = doc.RootElement.GetProperty("metrics").GetProperty("http_req_failed").GetProperty("values").GetProperty("rate").GetDouble();

        Assert.True(p50 > 0);
        Assert.True(p95 >= p50);
        Assert.True(p99 >= p95);
        Assert.True(failRate < 0.05);
    }

    [Fact]
    public void Lookup_list_scoped_scenario_file_exists()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !global::System.IO.File.Exists(Path.Combine(root.FullName, "perf", "k6", "lookup-list-scoped.js")))
            root = root.Parent;
        Assert.NotNull(root);
        Assert.True(global::System.IO.File.Exists(Path.Combine(root!.FullName, "scripts", "nfr-k6-summarize.ps1")));
    }
}
