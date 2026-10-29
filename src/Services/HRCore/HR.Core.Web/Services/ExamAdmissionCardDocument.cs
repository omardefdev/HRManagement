using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HRManagement.Documents.QuestPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace HR.Core.Web.Services;

/// <summary>
/// HCM-031 (PLAN-31, AC-HCM-31b): Arabic RTL exam admission card rendered from <see cref="ExamAdmissionCardView"/>, with
/// the verification code of the hash stored at issue. Carries no national id.
/// </summary>
public static class ExamAdmissionCardDocument
{
    public const string Title = "بطاقة دخول الامتحان";

    public static byte[] Generate(ExamAdmissionCardView card)
    {
        HrQuestPdfBootstrap.EnsureInitialized();
        var d = card.Data;
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ConfigureOfficialPage(Title);
                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);
                    col.Item().AlignCenter().Text($"{d.CompetitionNumber} — {d.CompetitionTitle}").Bold().FontSize(13);
                    col.Item().AlignCenter().Text($"الرقم الامتحاني: {d.ExamNumber}").Bold().FontSize(20);
                    col.Item().FieldTable(
                    [
                        ("اسم المتقدم", d.CandidateName),
                        ("نوع الامتحان", d.ExamTypeLabel),
                        ("الجلسة", d.SessionTitle),
                        ("الموعد", ExamAdmissionCardData.FormatDateTime(d.ScheduledAtUtc)),
                        ("المدة", $"{d.DurationMinutes} دقيقة"),
                        ("المكان", d.Venue),
                        ("رقم المقعد", d.SeatNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    ]);
                    col.Item().Text("التعليمات").Bold().FontSize(14);
                    col.Item().Text(d.InstructionsAr);
                    col.Item().PaddingTop(16).LineHorizontal(0.5f);
                    col.Item().Text(text =>
                    {
                        text.Span("رمز التحقق: ").Bold();
                        text.Span(card.VerificationCode ?? "—");
                    });
                    if (card.IsIssued && !card.IsIntact)
                    {
                        col.Item().Background(Colors.Red.Lighten4).Padding(6)
                            .Text("تنبيه: بيانات هذه البطاقة لا تطابق البصمة المسجَّلة عند إصدارها.").Bold();
                    }
                });
            });
        }).GeneratePdf();
    }
}
