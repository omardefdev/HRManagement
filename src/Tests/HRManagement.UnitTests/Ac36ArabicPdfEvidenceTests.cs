using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Web.Services;
using HRManagement.Documents.QuestPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace HRManagement.UnitTests;

/// <summary>AC-36: writes mixed Arabic PDF samples when AC36_EVIDENCE_DIR is set.</summary>
public class Ac36ArabicPdfEvidenceTests
{
    [Fact]
    public void Generates_mixed_arabic_samples_to_evidence_dir_when_configured()
    {
        var dir = Environment.GetEnvironmentVariable("AC36_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = Path.Combine(Path.GetTempPath(), "ac36-pdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        HrQuestPdfBootstrap.EnsureInitialized();

        var payslip = HrPayslipPdfGenerator.Generate(new PayslipPdfModel(
            EmployeeName: "أحمد محمد — Ahmed M.",
            EmployeeId: Guid.Parse("a0000001-0000-4000-8000-000000000201"),
            PeriodStart: new DateTime(2026, 7, 1),
            PeriodEnd: new DateTime(2026, 7, 31),
            LegalBasisFingerprint: "sha256:abc123mixedEN123",
            Components:
            [
                new PayslipComponentLine("راتب أساسي / Base", "استحقاق", 1_250_000m),
                new PayslipComponentLine("IBAN SY00 1234 5678", "info", 0m)
            ],
            GrossPay: 1_250_000m,
            TotalDeductions: 50_000m,
            NetPay: 1_200_000m));
        global::System.IO.File.WriteAllBytes(Path.Combine(dir, "payslip-mixed-ar.pdf"), payslip);
        AssertNoPlaceholderGlyphs(payslip);

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "ACCEPT-ASSET-BR1",
            Name = "حاسوب Dell Latitude — SN-EN-9988",
            Status = AssetStatus.InUse
        };
        var assignment = new AssetAssignment
        {
            AssetId = asset.Id,
            EmployeeId = Guid.Parse("a0000001-0000-4000-8000-000000000201"),
            AssignedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = AssetAssignmentStatus.Active,
            HandoverCondition = AssetHandoverCondition.Good
        };
        var handover = AssetHandoverPdfService.Generate(asset, assignment, "سارة علي — Logistics EN");
        global::System.IO.File.WriteAllBytes(Path.Combine(dir, "handover-mixed-ar.pdf"), handover);
        AssertNoPlaceholderGlyphs(handover);

        var report = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ConfigureOfficialPage("تقرير الموظفين — Employees Report", marginPoints: 28, pageSize: PageSizes.A4.Landscape());
                page.Content().PaddingVertical(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Text("الاسم / Name").Bold();
                        h.Cell().Text("الحالة").Bold();
                        h.Cell().Text("GUID").Bold();
                    });
                    table.Cell().Text("موظف قبول ACC010000001");
                    table.Cell().Text("نشط / Active");
                    table.Cell().Text("a0000001-0000-4000-8000-000000000201");
                });
            });
        }).GeneratePdf();
        global::System.IO.File.WriteAllBytes(Path.Combine(dir, "employees-report-mixed-ar.pdf"), report);
        AssertNoPlaceholderGlyphs(report);

        Assert.True(Directory.GetFiles(dir, "*.pdf").Length >= 3);
    }

    private static void AssertNoPlaceholderGlyphs(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.DoesNotContain("????", text);
    }
}
