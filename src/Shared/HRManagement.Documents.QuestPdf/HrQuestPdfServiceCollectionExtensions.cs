using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.Documents.QuestPdf;

public static class HrQuestPdfServiceCollectionExtensions
{
    /// <summary>Registers Arabic PDF fonts at startup (Docker-safe, no system fonts).</summary>
    public static IServiceCollection AddHrQuestPdfDocuments(this IServiceCollection services)
    {
        HrQuestPdfBootstrap.EnsureInitialized();
        return services;
    }
}
