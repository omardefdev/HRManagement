using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.Documents.QuestPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace HR.Core.Web.Services;

/// <summary>
/// HCM-029 (D1): official Arabic offer letter, rendered from <see cref="OfferLetterDocumentView"/>. Prints the
/// verification code of the hash stored at issue. No QR code: no QR library is referenced in Directory.Packages.props.
/// </summary>
public static class OfferLetterDocument
{
    public const string Title = "خطاب عرض وظيفي";

    public const string LegalReferenceNote = "مرجع قانوني (SYR-001)";

    public static byte[] Generate(OfferLetterDocumentView document)
    {
        // The license and fonts must be registered before Document.Create (startup does it in the host; tests do not).
        HrQuestPdfBootstrap.EnsureInitialized();
        var d = document.Data;
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ConfigureOfficialPage(Title);
                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    if (!string.IsNullOrWhiteSpace(d.OrgUnitName))
                        col.Item().AlignCenter().Text(d.OrgUnitName).Bold().FontSize(13);

                    col.Item().FieldTable(
                    [
                        ("رقم العرض", d.OfferNumber),
                        ("تاريخ الإصدار", OfferLetterDocumentData.FormatDate(d.IssuedOn)),
                        ("تاريخ انتهاء الصلاحية", OfferLetterDocumentData.FormatDate(d.ExpiresOn))
                    ]);

                    col.Item().Text($"السيد/السيدة {d.CandidateName}،").FontSize(13);
                    col.Item().Text("يسرّنا أن نعرض عليكم التعيين وفق الشروط الآتية:");

                    col.Item().FieldTable(
                    [
                        ("المسمى الوظيفي", d.PositionTitle),
                        ("رمز المنصب", d.PositionCode ?? "—"),
                        ("الدرجة", d.GradeCode ?? "—"),
                        ("الراتب المعروض", OfferLetterDocumentData.FormatSalary(d.OfferedSalary)),
                        ("تاريخ المباشرة المقترح", OfferLetterDocumentData.FormatDate(d.ProposedStartDate)),
                        ("نوع التوظيف المقترح", d.EmploymentTypeLabel),
                        (LegalReferenceNote, d.LegalReferenceLabel)
                    ]);

                    col.Item().Text("الشروط").Bold().FontSize(14);
                    col.Item().Text(d.ConditionsAr);

                    col.Item().PaddingTop(16).LineHorizontal(0.5f);
                    col.Item().Text(text =>
                    {
                        text.Span("رمز التحقق: ").Bold();
                        text.Span(document.VerificationCode ?? "غير مسجَّل (عرض صادر قبل تفعيل البصمة)");
                    });
                    if (document.HasStoredHash && !document.IsIntact)
                    {
                        col.Item().Background(Colors.Red.Lighten4).Padding(6)
                            .Text("تنبيه: بيانات هذا المستند لا تطابق البصمة المسجَّلة عند إصدار العرض.").Bold();
                    }
                });
            });
        }).GeneratePdf();
    }
}
