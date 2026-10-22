using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.Documents.QuestPdf;
using HRManagement.SharedKernel.Localization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HR.Core.Web.Services;

/// <summary>HCM-028 (D): official Arabic minutes of a selection committee session (generated from locked data).</summary>
public static class SelectionCommitteeMinutesDocument
{
    public const string Title = "محضر لجنة الاختيار";

    public static byte[] Generate(SelectionCommitteeMinutesData minutes)
    {
        // The license and fonts must be registered before Document.Create (startup does it in the host; tests do not).
        HrQuestPdfBootstrap.EnsureInitialized();
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ConfigureOfficialPage(Title, marginPoints: 28, pageSize: PageSizes.A4.Landscape());
                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().FieldTable(
                    [
                        ("رقم طلب الشاغر", minutes.RequisitionNumber),
                        ("المسمى الوظيفي", minutes.JobTitle),
                        ("تاريخ الجلسة", minutes.CommitteeDate.ToLocalTime().ToString("yyyy/MM/dd")),
                        ("غرض الجلسة", EnumDisplayExtensions.GetDisplayName(minutes.Purpose)),
                        ("تاريخ التقديم للاعتماد", minutes.SubmittedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "—"),
                        ("تاريخ الاعتماد", minutes.ApprovedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "غير معتمد بعد")
                    ]);

                    col.Item().Text("أعضاء اللجنة").Bold().FontSize(14);
                    col.Item().Element(c => MembersTable(c, minutes.Members));

                    col.Item().Text("المرشحون والتصويت والتوصيات").Bold().FontSize(14);
                    col.Item().Element(c => CandidatesTable(c, minutes.Candidates));

                    col.Item().Text("مسار الاعتماد").Bold().FontSize(14);
                    col.Item().Element(c => ApprovalsTable(c, minutes.Approvals));
                });
            });
        }).GeneratePdf();
    }

    private static void MembersTable(IContainer container, IReadOnlyList<SelectionCommitteeMinutesMember> members) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
            });
            table.Header(h =>
            {
                HeaderCell(h.Cell(), "الاسم");
                HeaderCell(h.Cell(), "الدور");
                HeaderCell(h.Cell(), "منسحب");
            });
            foreach (var m in members)
            {
                BodyCell(table.Cell(), m.Name);
                BodyCell(table.Cell(), m.IsChair ? "رئيس اللجنة" : "عضو");
                BodyCell(table.Cell(), m.Recused ? "نعم" : "لا");
            }
        });

    private static void CandidatesTable(IContainer container, IReadOnlyList<SelectionCommitteeMinutesCandidate> candidates) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.RelativeColumn(1.2f);
                c.RelativeColumn(1);
                c.RelativeColumn(1);
                c.RelativeColumn(1);
                c.RelativeColumn(2);
                c.RelativeColumn(1.2f);
                c.RelativeColumn(4);
            });
            table.Header(h =>
            {
                HeaderCell(h.Cell(), "المرشح");
                HeaderCell(h.Cell(), "الدرجة");
                HeaderCell(h.Cell(), "موافق");
                HeaderCell(h.Cell(), "غير موافق");
                HeaderCell(h.Cell(), "ممتنع");
                HeaderCell(h.Cell(), "التوصية");
                HeaderCell(h.Cell(), "الترتيب المقترح");
                HeaderCell(h.Cell(), "التعليل");
            });
            foreach (var c in candidates)
            {
                BodyCell(table.Cell(), c.Name);
                BodyCell(table.Cell(), c.Score?.ToString("0.00") ?? "—");
                BodyCell(table.Cell(), $"{c.Votes.Agree} / {c.Votes.EligibleVoters}");
                BodyCell(table.Cell(), c.Votes.Disagree.ToString());
                BodyCell(table.Cell(), c.Votes.Abstain.ToString());
                BodyCell(table.Cell(), c.Recommendation is CommitteeRecommendation r ? EnumDisplayExtensions.GetDisplayName(r) : "—");
                BodyCell(table.Cell(), c.ProposedRank?.ToString() ?? "—");
                BodyCell(table.Cell(), c.Rationale ?? "—");
            }
        });

    private static void ApprovalsTable(IContainer container, IReadOnlyList<SelectionCommitteeMinutesApproval> approvals)
    {
        if (approvals.Count == 0)
        {
            container.Text("لم تُتخذ أي خطوة اعتماد بعد.");
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.RelativeColumn(1.5f);
                c.RelativeColumn(2.5f);
                c.RelativeColumn(2);
                c.RelativeColumn(4);
            });
            table.Header(h =>
            {
                HeaderCell(h.Cell(), "الخطوة");
                HeaderCell(h.Cell(), "الإجراء");
                HeaderCell(h.Cell(), "المعتمد");
                HeaderCell(h.Cell(), "التاريخ");
                HeaderCell(h.Cell(), "الملاحظات");
            });
            foreach (var a in approvals)
            {
                BodyCell(table.Cell(), $"{a.Level}. {a.StepName}");
                BodyCell(table.Cell(), EnumDisplayExtensions.GetDisplayName(a.Status));
                BodyCell(table.Cell(), a.ActorName ?? "—");
                BodyCell(table.Cell(), a.ActionAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
                BodyCell(table.Cell(), a.Comments ?? "—");
            }
        });
    }

    private static void HeaderCell(IContainer cell, string text) =>
        cell.Background(Colors.Grey.Lighten3).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1)
            .Padding(4).Text(text).Bold();

    private static void BodyCell(IContainer cell, string text) =>
        cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(text);
}
