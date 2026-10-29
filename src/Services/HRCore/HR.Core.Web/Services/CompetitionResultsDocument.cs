using System.Globalization;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HRManagement.Documents.QuestPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace HR.Core.Web.Services;

/// <summary>
/// HCM-031 (PLAN-31, AC-HCM-31d): the public results list PDF — exam numbers and outcomes only (no names, no scores),
/// the publication date, the objection deadline and the version's verification code.
/// </summary>
public static class CompetitionResultsDocument
{
    public const string Title = "النتائج المعلنة للمسابقة";

    public static string OutcomeAr(CompetitionResultOutcome outcome) => outcome switch
    {
        CompetitionResultOutcome.Passed => "ناجح",
        CompetitionResultOutcome.Failed => "غير ناجح",
        _ => "غائب"
    };

    public static byte[] Generate(RecruitmentCompetition competition, CompetitionResultVersion version, IReadOnlyList<PublicResultRow> rows, DateTime? objectionDeadlineUtc)
    {
        HrQuestPdfBootstrap.EnsureInitialized();
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ConfigureOfficialPage(Title);
                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(8);
                    col.Item().AlignCenter().Text($"{competition.Number} — {competition.Title}").Bold().FontSize(13);
                    col.Item().FieldTable(
                    [
                        ("رقم النسخة", version.VersionNo.ToString(CultureInfo.InvariantCulture)),
                        ("تاريخ النشر", version.PublishedAt?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "—"),
                        ("آخر موعد للاعتراض", objectionDeadlineUtc?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "—"),
                        ("رمز التحقق", string.IsNullOrWhiteSpace(version.Sha256) ? "—" : version.Sha256.Trim()[..12].ToUpperInvariant())
                    ]);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });
                        table.Header(h =>
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("الرقم الامتحاني").Bold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("النتيجة").Bold();
                        });
                        foreach (var row in rows)
                        {
                            table.Cell().BorderBottom(0.5f).Padding(3).Text(row.ExamNumber);
                            table.Cell().BorderBottom(0.5f).Padding(3).Text(OutcomeAr(row.Outcome));
                        }
                    });
                });
            });
        }).GeneratePdf();
    }
}
