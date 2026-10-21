using Admin.Logistics.Domain.Entities;

using Admin.Logistics.Infrastructure.Abstractions;

using HRManagement.Documents.QuestPdf;

using HRManagement.SharedKernel.Localization;

using QuestPDF.Fluent;



namespace Admin.Logistics.Web.Services;



public static class AssetHandoverPdfService

{

    public static byte[] Generate(Asset asset, AssetAssignment assignment, string? employeeName)

    {

        var doc = Document.Create(container =>

        {

            container.Page(page =>

            {

                page.ConfigureOfficialPage("محضر تسليم عهدة");



                page.Content().PaddingTop(16).Column(col =>

                {

                    col.Item().FieldTable(BuildRows(asset, assignment, employeeName));

                    col.Item().PaddingTop(20).AlignRight().Text($"تاريخ الإصدار: {DateTime.UtcNow:yyyy/MM/dd}")

                        .FontSize(10);

                });

            });

        });



        return doc.GeneratePdf();

    }



    private static IEnumerable<(string Label, string Value)> BuildRows(

        Asset asset,

        AssetAssignment assignment,

        string? employeeName)

    {

        yield return ("رقم العهدة", asset.TagNumber);

        yield return ("اسم الأصل", asset.Name);

        yield return ("الموظف", employeeName ?? assignment.EmployeeId.ToString());

        yield return ("تاريخ التسليم", assignment.AssignedAt.ToString("yyyy/MM/dd"));

        yield return ("إعادة متوقعة", assignment.DueBackDate?.ToString("yyyy/MM/dd") ?? "—");

        yield return ("الحالة عند التسليم", AssetAssignmentDisplay.FormatHandoverCondition(assignment));

        yield return ("الدليل", AssetAssignmentDisplay.FormatEvidenceSummary(assignment, asset));

        yield return ("حالة العهدة", EnumDisplayExtensions.GetDisplayName(assignment.Status));

    }

}

