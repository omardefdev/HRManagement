using System.Text;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Web.Services;
using HRManagement.Documents.QuestPdf;
using QuestPDF.Fluent;

namespace HRManagement.UnitTests;

public class ArabicPdfGenerationTests
{
    [Fact]
    public void HrQuestPdfBootstrap_registers_fonts_without_throw()
    {
        var ex = Record.Exception(() =>
        {
            HrQuestPdfBootstrap.EnsureInitialized();
            HrQuestPdfBootstrap.EnsureInitialized();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Generate_sample_arabic_pdf_contains_noto_font_and_no_placeholder_glyphs()
    {
        HrQuestPdfBootstrap.EnsureInitialized();

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ConfigureOfficialPage("محضر تسليم عهدة");
                page.Content().Text("نص عربي للاختبار");
            });
        }).GeneratePdf();

        var text = Encoding.Latin1.GetString(pdf);

        Assert.Contains("/ToUnicode", text, StringComparison.Ordinal);
        Assert.True(
            text.Contains("Noto", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Arabic", StringComparison.OrdinalIgnoreCase),
            "Expected embedded Arabic font metadata in PDF");

        Assert.DoesNotContain("????", text);
    }

    [Fact]
    public void AssetHandoverPdfService_renders_arabic_labels()
    {
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "TAG-001",
            Name = "حاسوب محمول",
            Status = AssetStatus.InUse
        };

        var assignment = new AssetAssignment
        {
            AssetId = asset.Id,
            EmployeeId = Guid.NewGuid(),
            AssignedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            DueBackDate = new DateTime(2027, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            Status = AssetAssignmentStatus.Active,
            HandoverCondition = AssetHandoverCondition.Good
        };

        var pdf = AssetHandoverPdfService.Generate(asset, assignment, "أحمد محمد");

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 1024, $"PDF too small ({pdf.Length} bytes); Arabic fonts may be missing.");
    }

    [Fact]
    public void Generates_mixed_arabic_english_numbers_without_placeholder_glyphs()
    {
        HrQuestPdfBootstrap.EnsureInitialized();

        var payslip = HrPayslipPdfGenerator.Generate(new PayslipPdfModel(
            EmployeeName: "ليلى Hassan — ليلى",
            EmployeeId: Guid.NewGuid(),
            PeriodStart: new DateTime(2026, 6, 1),
            PeriodEnd: new DateTime(2026, 6, 30),
            LegalBasisFingerprint: "mixed-en-123",
            Components: [new PayslipComponentLine("بدل نقل Transport", "استحقاق", 150_000m)],
            GrossPay: 150_000m,
            TotalDeductions: 0m,
            NetPay: 150_000m));

        var text = Encoding.Latin1.GetString(payslip);
        Assert.DoesNotContain("????", text);
        Assert.True(payslip.Length > 1024);

        var asset = new Asset { Id = Guid.NewGuid(), TagNumber = "MIX-001", Name = "جهاز Surface EN-2026", Status = AssetStatus.InUse };
        var assignment = new AssetAssignment
        {
            AssetId = asset.Id,
            EmployeeId = Guid.NewGuid(),
            AssignedAt = DateTime.UtcNow,
            Status = AssetAssignmentStatus.Active
        };
        var handover = AssetHandoverPdfService.Generate(asset, assignment, "English Name — اسم عربي");
        Assert.DoesNotContain("????", Encoding.Latin1.GetString(handover));
    }

    [Fact]
    public void Pdf_arabic_validate_script_passes()
    {
        var repoRoot = ResolveRepoRoot();
        var script = Path.Combine(repoRoot, "scripts", "pdf-arabic-validate.ps1");
        Assert.True(System.IO.File.Exists(script), "pdf-arabic-validate.ps1 must exist");

        // QA-034: RepoScriptRunner passes -ExecutionPolicy Bypass (Windows) / uses pwsh (Linux) and drains both streams.
        var (exitCode, stdout, stderr) = HRManagement.TestSupport.RepoScriptRunner.RunScript(script);
        Assert.True(exitCode == 0, $"pdf-arabic-validate.ps1 failed ({exitCode}):\n{stdout}\n{stderr}");
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "scripts", "pdf-arabic-validate.ps1")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root");
    }
}
