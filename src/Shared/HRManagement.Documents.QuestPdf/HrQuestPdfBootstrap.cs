using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace HRManagement.Documents.QuestPdf;

/// <summary>Registers embedded Arabic fonts and QuestPDF community license (once per process).</summary>
public static class HrQuestPdfBootstrap
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            return;

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var assembly = typeof(HrQuestPdfBootstrap).Assembly;
        RegisterEmbeddedFont(assembly, "HRManagement.Documents.QuestPdf.fonts.NotoSansArabic-Regular.ttf");
        RegisterEmbeddedFont(assembly, "HRManagement.Documents.QuestPdf.fonts.NotoSansArabic-Bold.ttf");
    }

    private static void RegisterEmbeddedFont(System.Reflection.Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded font resource: {resourceName}");
        FontManager.RegisterFont(stream);
    }
}
