using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRManagement.Documents.QuestPdf;

public static class HrOfficialDocumentLayout
{
    public static PageDescriptor ConfigureOfficialPage(
        this PageDescriptor page,
        string documentTitle,
        string? organizationName = null,
        float marginPoints = 40,
        PageSize? pageSize = null)
    {
        page.ConfigureArabicPage(marginPoints, pageSize: pageSize);

        var org = string.IsNullOrWhiteSpace(organizationName)
            ? HrOfficialDocumentDefaults.OrganizationNameAr
            : organizationName.Trim();

        page.Header().Column(col =>
        {
            col.Item().AlignCenter().Text(org).Bold().FontSize(14);
            col.Item().PaddingTop(8).AlignCenter().Text(documentTitle).Bold().FontSize(18);
            col.Item().PaddingTop(6).LineHorizontal(1);
        });

        page.Footer().AlignCenter().DefaultTextStyle(x => x.FontSize(10)).Text(text =>
        {
            text.Span("صفحة ");
            text.CurrentPageNumber();
            text.Span(" من ");
            text.TotalPages();
        });

        return page;
    }

    public static void FieldTable(this IContainer container, IEnumerable<(string Label, string Value)> rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                columns.RelativeColumn(3);
            });

            foreach (var (label, value) in rows)
            {
                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(6)
                    .Text(label).Bold();
                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(6)
                    .Text(string.IsNullOrWhiteSpace(value) ? "—" : value);
            }
        });
    }
}
