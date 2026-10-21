using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-36: generate and structurally verify the Arabic PDF evidence bundle.</summary>
public class Ac36ArabicPdfLiveTests
{
    [SkippableFact]
    public async Task Arabic_pdf_samples_are_generated_and_have_valid_pdf_headers()
    {
        AcceptanceEnv.RequireEnabled();
        var root = AcceptanceHarness.RepoRoot;
        var recorder = new EvidenceRecorder("AC-36", Guid.NewGuid().ToString("D"));
        recorder.WriteMetadata("AC-36 Arabic PDF visual", "ops");

        Assert.True(File.Exists(Path.Combine(root, "scripts", "acceptance-pdf-arabic.ps1")));
        Assert.True(File.Exists(Path.Combine(root, "docs", "acceptance-plans", "checklists", "AC-36-arabic-pdf-visual.md")));

        var startedAt = DateTime.UtcNow;
        var (exitCode, output) = await AcceptanceMessagingHelper.RunScriptAsync(
            "acceptance-pdf-arabic.ps1",
            ("EvidenceDir", recorder.Folder));
        Assert.Equal(0, exitCode);

        var sampleDirectory = Path.Combine(recorder.Folder, "samples");
        var expectedFiles = new[]
        {
            "payslip-mixed-ar.pdf",
            "handover-mixed-ar.pdf",
            "employees-report-mixed-ar.pdf"
        };
        var checks = new List<object>();
        foreach (var fileName in expectedFiles)
        {
            var path = Path.Combine(sampleDirectory, fileName);
            Assert.True(File.Exists(path), $"Expected Arabic PDF sample is missing: {fileName}\n{output}");
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.True(bytes.Length > 1_000, $"Generated PDF sample is unexpectedly small: {fileName}");
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
            checks.Add(new { fileName, bytes = bytes.Length, validHeader = true });
        }

        var checklistPath = Path.Combine(recorder.Folder, "review-checklist.md");
        Assert.True(File.Exists(checklistPath));
        recorder.WriteAfter(new
        {
            generatedSamples = expectedFiles.Length,
            automatedStructureChecksPassed = true,
            humanVisualSignOffRequired = true,
            checklist = checklistPath
        });
        recorder.WriteDbChecks(new { scriptExitCode = exitCode, samples = checks, scriptOutput = output });
        recorder.RecordTiming("elapsedMs", (DateTime.UtcNow - startedAt).TotalMilliseconds);
        recorder.FlushTiming();
        recorder.WriteResult("Pass");
    }
}
