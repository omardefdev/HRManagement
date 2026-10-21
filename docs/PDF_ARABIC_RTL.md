# PDF Arabic & RTL (QuestPDF)

Arabic PDFs in HR Management use **QuestPDF** with embedded **Noto Sans Arabic** fonts and shared RTL layout helpers. Without this layer, QuestPDF’s default font (Liberation Sans) renders Arabic text as `?` — especially in Docker/Linux where no system Arabic fonts exist.

## Shared library

Project: `src/Shared/HRManagement.Documents.QuestPdf/`

| Component | Purpose |
|-----------|---------|
| `HrQuestPdfBootstrap` | Community license + one-time `FontManager.RegisterFont` for embedded TTF files |
| `HrArabicDocumentExtensions.ConfigureArabicPage` | A4, margins, `Noto Sans Arabic`, `ContentFromRightToLeft()` |
| `HrOfficialDocumentLayout.ConfigureOfficialPage` | Official header (org + title), footer (“صفحة X من Y”), field table helper |
| `HrPayslipPdfGenerator` | Payslip PDF builder for Payroll |
| `AddHrQuestPdfDocuments()` | Call in `Program.cs` so fonts register at startup |

Fonts live under `fonts/` as **EmbeddedResource** (OFL — Google Noto Sans Arabic).

## Adding a new PDF

1. Add `ProjectReference` to `HRManagement.Documents.QuestPdf` (do not reference QuestPDF directly from web projects unless necessary).
2. Call `builder.Services.AddHrQuestPdfDocuments()` in `Program.cs`.
3. In the generator, always use one of:
   - `page.ConfigureOfficialPage("عنوان المستند")` for formal documents, or
   - `page.ConfigureArabicPage()` for custom layouts.
4. Use `EnumDisplayExtensions.GetDisplayName(...)` for enum labels (see L10N-003).
5. Run `scripts/pdf-arabic-validate.ps1` and `ArabicPdfGenerationTests`.

Example:

```csharp
var doc = Document.Create(container =>
{
    container.Page(page =>
    {
        page.ConfigureOfficialPage("عنوان التقرير");
        page.Content().PaddingTop(16).Column(col =>
        {
            col.Item().FieldTable([
                ("الحقل", "القيمة"),
            ]);
        });
    });
});
return doc.GeneratePdf();
```

## Current consumers

| Service | Generator |
|---------|-----------|
| Admin Logistics | `AssetHandoverPdfService` — handover receipt |
| HR Core | `ReportsController.ExportEmployeesPdf` |
| Payroll | `HrPayslipPdfGenerator` — single & bulk payslips |

## CI validation

```powershell
./scripts/pdf-arabic-validate.ps1
```

Fails if any `.cs` file under `src/` uses `Document.Create` / `GeneratePdf` outside `HRManagement.Documents.QuestPdf` without the shared Arabic helpers.

## Relation to DOC-001

`OfficialDocumentTemplate` in SharedKernel defines domain metadata for official documents (PDF/A, QR, signature). This library provides the **Arabic presentation layer** only. PDF/A compliance and verification features will build on `HrOfficialDocumentLayout` in a later DOC-001 phase.

## License

Noto Sans Arabic is licensed under the [SIL Open Font License 1.1](https://scripts.sil.org/OFL).
