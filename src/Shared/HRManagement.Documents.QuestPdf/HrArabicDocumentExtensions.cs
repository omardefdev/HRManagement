using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HRManagement.Documents.QuestPdf;

public static class HrArabicDocumentExtensions
{
    public static TextStyle ArabicTextStyle(this TextStyle style, float fontSize = 12) =>
        style.FontFamily(HrQuestPdfFonts.ArabicFamily).FontSize(fontSize);

    /// <summary>RTL page defaults: A4, margins, Noto Sans Arabic, right-to-left content flow.</summary>
    public static PageDescriptor ConfigureArabicPage(
        this PageDescriptor page,
        float marginPoints = 40,
        float defaultFontSize = 12,
        PageSize? pageSize = null)
    {
        HrQuestPdfBootstrap.EnsureInitialized();

        page.Size(pageSize ?? PageSizes.A4);
        page.Margin(marginPoints);
        page.DefaultTextStyle(x => x.ArabicTextStyle(defaultFontSize));
        page.ContentFromRightToLeft();
        return page;
    }
}
