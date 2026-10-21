namespace HRManagement.AcceptanceTests;

public class AcceptanceEvidenceSummaryTests
{
    [Fact]
    public void Recorder_completes_contract_and_preserves_detailed_result()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hr-acceptance-recorder-" + Guid.NewGuid().ToString("N"));
        try
        {
            var recorder = new EvidenceRecorder("AC-98", "corr-98", tempRoot);
            recorder.WriteMetadata("derived evidence contract", "persona-98");
            recorder.CaptureHttp("lookup", "GET", "/api/example", 200, new { id = 98 });
            File.WriteAllText(Path.Combine(recorder.Folder, "result.json"), """{"ok":true,"measured":98}""");

            recorder.WriteResult("Pass");

            var report = AcceptanceEvidenceSummary.Rebuild(tempRoot);
            var entry = Assert.Single(report.Entries);
            Assert.True(entry.ContractComplete);

            using var result = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(recorder.Folder, "result.json")));
            Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(98, result.RootElement.GetProperty("measured").GetInt32());
            Assert.Equal("Pass", result.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Rebuild_creates_summary_markdown()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "hr-acceptance-" + Guid.NewGuid().ToString("N"));
        try
        {
            var acFolder = Path.Combine(tempRoot, "AC-99");
            Directory.CreateDirectory(acFolder);
            Directory.CreateDirectory(Path.Combine(acFolder, "http"));
            foreach (var file in new[] { "metadata.json", "steps.md", "before.json", "after.json",
                         "audit.json", "db-checks.json", "timing.json" })
            {
                File.WriteAllText(Path.Combine(acFolder, file), "{}");
            }
            File.WriteAllText(Path.Combine(acFolder, "result.json"),
                """{"status":"Pass","reason":"unit test stub"}""");

            var report = AcceptanceEvidenceSummary.Rebuild(tempRoot);
            Assert.True(File.Exists(report.SummaryPath));
            Assert.Single(report.Entries);
            Assert.Equal("Pass", report.Entries[0].Status);
            Assert.True(report.Entries[0].ContractComplete);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
