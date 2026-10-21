using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace HRManagement.Documents.QuestPdf;

public sealed record PayslipComponentLine(string Name, string TypeLabel, decimal Amount);

public sealed record PayslipPdfModel(
    string EmployeeName,
    Guid EmployeeId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    string? LegalBasisFingerprint,
    IReadOnlyList<PayslipComponentLine> Components,
    decimal GrossPay,
    decimal TotalDeductions,
    decimal NetPay);

public static class HrPayslipPdfGenerator
{
    public static byte[] Generate(PayslipPdfModel model)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ConfigureOfficialPage("كشف راتب", marginPoints: 30);

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Item().FieldTable([
                        ("الموظف", model.EmployeeName),
                        ("المعرّف", model.EmployeeId.ToString()),
                        ("الفترة", $"{model.PeriodStart:yyyy/MM/dd} — {model.PeriodEnd:yyyy/MM/dd}"),
                        .. model.LegalBasisFingerprint is { Length: > 0 } hash
                            ? new[] { ("الأساس القانوني (بصمة)", hash[..Math.Min(16, hash.Length)] + "…") }
                            : Array.Empty<(string, string)>()
                    ]);

                    col.Item().PaddingTop(16).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("البند").Bold();
                            h.Cell().Text("النوع").Bold();
                            h.Cell().AlignRight().Text("المبلغ").Bold();
                        });
                        foreach (var comp in model.Components)
                        {
                            table.Cell().Text(comp.Name);
                            table.Cell().Text(comp.TypeLabel);
                            table.Cell().AlignRight().Text(comp.Amount.ToString("N2"));
                        }
                    });

                    col.Item().PaddingTop(12).LineHorizontal(1);
                    col.Item().PaddingTop(8).Row(r =>
                    {
                        r.RelativeItem().Text("الإجمالي:").Bold();
                        r.ConstantItem(120).AlignRight().Text(model.GrossPay.ToString("N2")).Bold();
                    });
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("الخصومات:");
                        r.ConstantItem(120).AlignRight().Text(model.TotalDeductions.ToString("N2"));
                    });
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("الصافي:").Bold().FontSize(14);
                        r.ConstantItem(120).AlignRight().Text(model.NetPay.ToString("N2")).Bold().FontSize(14);
                    });
                });
            });
        });

        return doc.GeneratePdf();
    }
}
