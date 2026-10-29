using ClosedXML.Excel;
using HR.Core.Domain.Services.RecruitmentKpi;
using HR.Core.Web.ViewModels;
using HRManagement.Documents.QuestPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using F = HR.Core.Web.ViewModels.RecruitmentReportFormat;

namespace HR.Core.Web.Services;

/// <summary>
/// HCM-032 (PLAN-32): the aggregate recruitment report as an Excel workbook (ClosedXML) and a PDF summary (QuestPDF,
/// official Arabic RTL layout). Every sheet starts with the REP-001 provenance block — period, scope, generation time
/// (Gregorian, invariant), definitions version and source. Aggregates only: no candidate name and no national id.
/// Numbers are numeric cells; dates are <c>yyyy-MM-dd</c> text so no spreadsheet locale can turn them Hijri.
/// </summary>
public static class RecruitmentReportDocuments
{
    public const string Source = "HR.Core — طلبات التوظيف وسجل أحداثها، العروض، الاستئنافات، مسارات الاعتماد، الملاك";

    public static IReadOnlyList<(string Label, string Value)> Provenance(RecruitmentReportsViewModel vm, string scopeLabel) =>
    [
        ("التقرير", "تقارير التوظيف"),
        ("الفترة (UTC)", F.PeriodLabel(vm.Filter)),
        ("النطاق", scopeLabel),
        ("وقت الإنشاء", F.DateTimeUtc(vm.GeneratedAt)),
        ("إصدار التعريفات", vm.DefinitionsVersion),
        ("المصدر", Source)
    ];

    public static byte[] Workbook(RecruitmentReportsViewModel vm, string scopeLabel)
    {
        using var workbook = new XLWorkbook();
        var provenance = Provenance(vm, scopeLabel);

        var kpis = Sheet(workbook, "المؤشرات", provenance, out var row);
        Header(kpis, row++, "المؤشر", "القيمة", "n", "التغطية", "ملاحظة");
        foreach (var (name, value, n, coverage, note) in KpiRows(vm))
            Row(kpis, row++, name, value, n, coverage, note);

        var funnel = Sheet(workbook, "القمع", provenance, out row);
        Header(funnel, row++, "المرحلة", "العدد");
        foreach (var stage in vm.Funnel.Stages)
            Row(funnel, row++, F.StageLabel(stage.Stage), stage.Count);
        row++;
        Header(funnel, row++, "الحالة الحالية", "العدد");
        foreach (var status in vm.Funnel.CurrentStatuses)
            Row(funnel, row++, F.Label(status.Status), status.Count);
        row++;
        Header(funnel, row++, "الحالة", "وسيط الأيام", "n");
        foreach (var stage in vm.StageTime.Rows)
            Row(funnel, row++, F.Label(stage.Status), stage.MedianDays, stage.N);
        row++;
        Header(funnel, row++, "القناة", "الطلبات", "المعيّنون", "نسبة التعيين %");
        foreach (var channel in vm.Channels.Rows)
            Row(funnel, row++, F.Label(channel.Channel), channel.Applied, channel.Hired, channel.HireRate);

        var requisitions = Sheet(workbook, "طلبات الشاغر", provenance, out row);
        Header(requisitions, row++, "الطلب", "الوظيفة", "الوحدة", "الحالة", "تاريخ الطلب", "الاعتماد", "النشر", "الإغلاق",
            "العمر (أيام)", "المتقدمون", "قائمة مختصرة", "مقابلة", "عروض", "تعيين", "زمن الملء (أيام)");
        foreach (var r in vm.Requisitions.Rows)
            Row(requisitions, row++, r.RequisitionNumber, r.JobTitle, r.OrgUnitName, F.Label(r.Status), F.Date(r.RequestedDate),
                F.Date(r.ApprovedDate), F.Date(r.PublishedAt), F.Date(r.EndedAt), r.AgeDays, r.Applicants, r.Stages[2].Count,
                r.Stages[3].Count, r.Stages[6].Count, r.Stages[8].Count, r.TimeToFillDays);

        var approvals = Sheet(workbook, "الاعتمادات المتأخرة", provenance, out row);
        Header(approvals, row++, "الكيان", "المسار", "رمز المسار", "الخطوة", "الدور", "بداية الخطوة", "العمر (ساعات)", "المهلة (ساعات)", "مصدر المهلة");
        foreach (var o in vm.Approvals.Overdue)
            Row(approvals, row++, o.EntityLabel, o.WorkflowName, o.WorkflowCode, o.StepName, o.RoleRequired, F.DateTimeUtc(o.StepStartedAt),
                o.AgeHours, o.SlaHours, F.SlaSourceLabel(o.SlaSource));
        row++;
        Header(approvals, row++, "المسار", "الخطوة", "الوسيط (ساعات)", "P90 (ساعات)", "n");
        foreach (var s in vm.Approvals.CompletedSteps)
            Row(approvals, row++, s.WorkflowName, $"{s.StepLevel} — {s.StepName}", s.MedianHours, s.P90Hours, s.N);

        var offers = Sheet(workbook, "العروض", provenance, out row);
        Header(offers, row++, "البند", "العدد");
        Row(offers, row++, "مقبول", vm.Offers.Accepted);
        Row(offers, row++, "مرفوض", vm.Offers.Declined);
        Row(offers, row++, "منتهي الصلاحية", vm.Offers.Expired);
        Row(offers, row++, "ملغى (خارج النسبة)", vm.Offers.Revoked);
        Row(offers, row++, "عروض أُنشئت", vm.Merit.OffersCreated);
        Row(offers, row++, "تجاوز جدارة", vm.Merit.MeritOverrides);
        Row(offers, row++, "عروض استثنائية", vm.Merit.ExceptionOffers);
        row++;
        Header(offers, row++, "فئة سبب الرفض / جهة الرفض", "العدد");
        foreach (var c in vm.Declines.ByCategory)
            Row(offers, row++, c.Label, c.Count);
        foreach (var c in vm.Declines.ByDeclinedBy)
            Row(offers, row++, "بواسطة: " + c.Label, c.Count);

        var appeals = Sheet(workbook, "الاستئنافات", provenance, out row);
        Header(appeals, row++, "البند", "العدد");
        Row(appeals, row++, "استئنافات مقدمة", vm.Appeals.AppealsFiled);
        Row(appeals, row++, "رفض قابل للاستئناف", vm.Appeals.AppealableRejections);
        foreach (var o in vm.Appeals.DecidedOutcomes)
            Row(appeals, row++, o.Label, o.Count);
        Row(appeals, row++, "اعتراضات مقدمة", vm.Appeals.ObjectionsFiled);
        Row(appeals, row++, "اعتراضات مقبولة", vm.Appeals.ObjectionsUpheld);
        Row(appeals, row++, "اعتراضات مرفوضة", vm.Appeals.ObjectionsDismissed);

        var vacancies = Sheet(workbook, "الشواغر المفتوحة", provenance, out row);
        Header(vacancies, row++, "المنصب", "الدرجة", "الوحدة", "شاغر منذ", "أيام");
        foreach (var v in vm.Vacancies.VacantWithoutRequisition)
            Row(vacancies, row++, v.PositionCode, v.GradeCode, v.OrgUnitName, F.Date(v.VacantSince), v.DaysVacant);
        row++;
        Header(vacancies, row++, "طلب منشور", "الوظيفة", "الوحدة", "النشر", "أيام");
        foreach (var s in vm.Vacancies.StalePublished)
            Row(vacancies, row++, s.RequisitionNumber, s.JobTitle, s.OrgUnitName, F.Date(s.PublishedAt), s.DaysPublished);

        if (vm.Competitions.Visible)
        {
            var competitions = Sheet(workbook, "المسابقات", provenance, out row);
            Header(competitions, row++, "المسابقة", "العنوان", "الحالة", "المتقدمون", "المقبولون للاختبار", "الحاضرون", "الناجحون",
                "المخصَّصون", "الاحتياط", "المستدعَون", "اعتراضات", "اعتراضات مقبولة");
            foreach (var c in vm.Competitions.Rows)
                Row(competitions, row++, c.Number, c.Title, F.Label(c.Status), c.Applicants, c.Admitted, c.Present, c.Passed,
                    c.Allocated, c.Reserve, c.Recalled, c.ObjectionsFiled, c.ObjectionsUpheld);
        }

        foreach (var sheet in workbook.Worksheets)
            sheet.Columns().AdjustToContents(1, 200);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] Pdf(RecruitmentReportsViewModel vm, string scopeLabel)
    {
        HrQuestPdfBootstrap.EnsureInitialized();
        var provenance = Provenance(vm, scopeLabel);
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ConfigureOfficialPage("تقرير مؤشرات التوظيف", marginPoints: 28, pageSize: PageSizes.A4.Landscape());
                page.Content().PaddingVertical(8).Column(column =>
                {
                    column.Spacing(10);
                    column.Item().FieldTable(provenance);
                    column.Item().Text("المؤشرات الرئيسية").Bold().FontSize(13);
                    column.Item().Element(c => Table(c, ["المؤشر", "القيمة", "n", "التغطية", "ملاحظة"],
                        KpiRows(vm).Select(k => new[] { k.Name, Text(k.Value), Text(k.N), Text(k.Coverage), k.Note })));
                    column.Item().Text("قمع التوظيف").Bold().FontSize(13);
                    column.Item().Element(c => Table(c, ["المرحلة", "العدد"],
                        vm.Funnel.Stages.Select(s => new[] { F.StageLabel(s.Stage), F.Number(s.Count) })));
                    column.Item().Text("الاعتمادات المتأخرة").Bold().FontSize(13);
                    column.Item().Element(c => Table(c, ["الكيان", "المسار", "الخطوة", "الدور", "العمر (ساعات)", "المهلة (ساعات)"],
                        vm.Approvals.Overdue.Select(o => new[]
                        {
                            o.EntityLabel, o.WorkflowName, o.StepName, o.RoleRequired, F.Value(o.AgeHours), F.Number(o.SlaHours)
                        })));
                    column.Item().Text("الشواغر المفتوحة").Bold().FontSize(13);
                    column.Item().Element(c => Table(c, ["البند", "العدد"],
                    [
                        ["مناصب شاغرة بلا طلب", F.Number(vm.Vacancies.VacantWithoutRequisitionCount)],
                        [$"طلبات منشورة منذ أكثر من {F.Number(vm.Vacancies.StalePublishedDays)} يوماً", F.Number(vm.Vacancies.StalePublishedCount)]
                    ]));
                });
            });
        });
        return document.GeneratePdf();
    }

    /// <summary>The KPI summary rows shared by the workbook and the PDF.</summary>
    public static IReadOnlyList<(string Name, object? Value, int? N, decimal? Coverage, string Note)> KpiRows(RecruitmentReportsViewModel vm) =>
    [
        ("طلبات الفترة", vm.Funnel.CohortCount, vm.Funnel.CohortCount, vm.Funnel.CoveragePercent, "التغطية = نسبة الطلبات ذات سجل الأحداث"),
        ("زمن الملء — الوسيط (أيام)", vm.TimeToFill.Days.Median, vm.TimeToFill.Days.N, vm.TimeToFill.Days.CoveragePercent,
            $"بديل البداية {F.Number(vm.TimeToFill.StartFallbackCount)}، بديل التعيين {F.Number(vm.TimeToFill.HiredFallbackCount)}"),
        ("زمن الملء — P90 (أيام)", vm.TimeToFill.Days.P90, vm.TimeToFill.Days.N, vm.TimeToFill.Days.CoveragePercent, ""),
        ("زمن التعيين — الوسيط (أيام)", vm.TimeToHire.Median, vm.TimeToHire.N, vm.TimeToHire.CoveragePercent,
            $"قيم بديلة {F.Number(vm.TimeToHire.FallbackCount)}"),
        ("زمن التعيين — P90 (أيام)", vm.TimeToHire.P90, vm.TimeToHire.N, vm.TimeToHire.CoveragePercent, ""),
        ("نسبة قبول العروض %", vm.Offers.AcceptanceRate, vm.Offers.Decided, null, $"الملغاة مستبعدة ({F.Number(vm.Offers.Revoked)})"),
        ("معدل الاستئناف %", vm.Appeals.AppealRate, vm.Appeals.AppealableRejections, null, ""),
        ("تجاوزات الجدارة %", vm.Merit.MeritOverrideRate, vm.Merit.OffersCreated, null, ""),
        ("العروض الاستثنائية %", vm.Merit.ExceptionRate, vm.Merit.OffersCreated, null, ""),
        ("مناصب شاغرة بلا طلب", vm.Vacancies.VacantWithoutRequisitionCount, null, null, "حتى " + F.Date(vm.Vacancies.AsOf)),
        ("طلبات منشورة متقادمة", vm.Vacancies.StalePublishedCount, null, null, $"أكثر من {F.Number(vm.Vacancies.StalePublishedDays)} يوماً"),
        ("اعتمادات متأخرة", vm.Approvals.OverdueCount, vm.Approvals.InProgressCount, null, "المهلة قيمة سياسة تنظيمية مؤقتة")
    ];

    private static IXLWorksheet Sheet(XLWorkbook workbook, string name, IReadOnlyList<(string Label, string Value)> provenance, out int nextRow)
    {
        var sheet = workbook.Worksheets.Add(name);
        sheet.RightToLeft = true;
        var row = 1;
        foreach (var (label, value) in provenance)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = value;
            row++;
        }
        nextRow = row + 1;
        return sheet;
    }

    private static void Header(IXLWorksheet sheet, int row, params string[] labels)
    {
        for (var i = 0; i < labels.Length; i++)
            sheet.Cell(row, i + 1).Value = labels[i];
        var range = sheet.Range(row, 1, row, labels.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    private static void Row(IXLWorksheet sheet, int row, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var cell = sheet.Cell(row, i + 1);
            switch (values[i])
            {
                case null:
                    cell.Value = KpiStatistics.NoValue;
                    break;
                case int n:
                    cell.Value = n;
                    break;
                case decimal d:
                    cell.Value = d;
                    break;
                default:
                    cell.Value = values[i]!.ToString();
                    break;
            }
        }
    }

    private static string Text(object? value) => value switch
    {
        null => KpiStatistics.NoValue,
        int n => F.Number(n),
        decimal d => F.Value(d),
        _ => value.ToString() ?? ""
    };

    private static void Table(QuestPDF.Infrastructure.IContainer container, string[] headers, IEnumerable<string[]> rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in headers)
                    columns.RelativeColumn();
            });
            table.Header(header =>
            {
                foreach (var h in headers)
                    header.Cell().BorderBottom(1).PaddingVertical(4).Text(h).Bold();
            });
            var any = false;
            foreach (var row in rows)
            {
                any = true;
                foreach (var cell in row)
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).Text(string.IsNullOrEmpty(cell) ? "—" : cell);
            }
            if (!any)
                table.Cell().ColumnSpan((uint)headers.Length).PaddingVertical(3).Text("لا بيانات.");
        });
    }
}
