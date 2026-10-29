using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Web.Services;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-031 (PLAN-31, P3, AC-HCM-31b): admission card PDF, stable hash and verification code, no national id.</summary>
public sealed class Hcm031AdmissionCardTests
{
    [Fact]
    public async Task Issued_cards_carry_a_stable_hash_and_render_a_pdf_without_the_national_id()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (competition, _) = await SeedExamReadyAsync(db, 2, 2);
        var seating = CreateSeating(db);
        Assert.True((await seating.SeatAsync(competition.Id, Creator)).Success);
        Assert.True((await seating.IssueAdmissionCardsAsync(competition.Id, Creator)).Success);
        var candidate = await db.ExamCandidates.AsNoTracking().FirstAsync();
        var cards = new ExamAdmissionCardDocumentService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));

        var card = await cards.GetAsync(candidate.Id);

        Assert.NotNull(card);
        Assert.True(card.IsIssued);
        Assert.True(card.IsIntact);
        Assert.Equal(64, candidate.AdmissionCardSha256!.Trim().Length);
        Assert.Equal(candidate.AdmissionCardSha256.Trim()[..12].ToUpperInvariant(), card.VerificationCode);
        Assert.Equal(card.CurrentSha256, (await cards.GetAsync(candidate.Id))!.CurrentSha256);
        Assert.Equal(candidate.ExamNumber, card.Data.ExamNumber);

        var application = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == candidate.RecruitmentApplicationId);
        var canonical = ExamAdmissionCardDocumentService.CanonicalJson(card.Data);
        Assert.DoesNotContain(application.NationalId!, canonical, StringComparison.Ordinal);
        Assert.DoesNotContain(application.NationalIdHash!, canonical, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(ExamAdmissionCardData).GetProperties(), p => p.Name.Contains("National", StringComparison.Ordinal));

        var pdf = ExamAdmissionCardDocument.Generate(card);
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task A_changed_seat_after_issue_shows_the_card_is_no_longer_intact()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var (competition, _) = await SeedExamReadyAsync(db, 1, 2);
        var seating = CreateSeating(db);
        Assert.True((await seating.SeatAsync(competition.Id, Creator)).Success);
        Assert.True((await seating.IssueAdmissionCardsAsync(competition.Id, Creator)).Success);
        var candidate = await db.ExamCandidates.FirstAsync();
        candidate.SeatNumber = 2;
        await db.SaveChangesAsync();

        var card = await new ExamAdmissionCardDocumentService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())).GetAsync(candidate.Id);

        Assert.False(card!.IsIntact);
        Assert.NotNull(card.VerificationCode);
    }

    [Fact]
    public void The_canonical_json_is_culture_independent()
    {
        var data = new ExamAdmissionCardData("CMP-2026-00001", "مسابقة", "اسم", "12345678", "كتابي", "القاعة 1",
            new DateTime(2026, 11, 10, 7, 30, 0, DateTimeKind.Utc), 120, "المبنى أ", 7, "تعليمات");
        var invariant = ExamAdmissionCardDocumentService.ComputeSha256(data);
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.Equal(invariant, ExamAdmissionCardDocumentService.ComputeSha256(data));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
        Assert.Contains("\"scheduledAt\":\"2026-11-10T07:30:00Z\"", ExamAdmissionCardDocumentService.CanonicalJson(data), StringComparison.Ordinal);
    }
}
