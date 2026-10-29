using System.Text.Json;
using System.Text.RegularExpressions;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Controllers;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-30 K2: the HR.Core DSAR candidate slices (<c>candidates/{id}</c> and <c>candidates/by-hash/{hash}</c>) carry the
/// profile, intake channel, eligibility flags, document metadata, the message log, the offers' acceptance channel and the
/// /careers/my code challenges as counts — never a code hash, request address, message body, salary or an 11-digit run (the
/// Compliance collector drops any array that holds one). Serialized as ASP.NET Core does (web defaults); no culture input.
/// </summary>
public class Hcm030DsarCandidateExportTests
{
    private const string CodeHmac = "c0dec0dec0dec0dec0dec0dec0dec0dec0dec0dec0dec0dec0dec0dec0dec0de";
    private const string RequestIpHash = "1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f";
    private const string RawNationalIdInFileName = "04011122233";

    [Fact]
    public async Task Candidate_by_citizen_id_exports_profile_records_and_otp_counts_only()
    {
        await using var db = Hcm030RetentionJobTests.CreateContext();
        var pair = await SeedAsync(db);

        var json = Serialize(await new DsarInternalController(db).GetCandidateByApplication(pair.Citizen.Id, default));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("CitizenApplication", root.GetProperty("type").GetString());
        Assert.Equal("تخصص هندسة", root.GetProperty("specialization").GetString());
        Assert.Equal("BACH", root.GetProperty("educationLevelCode").GetString());
        Assert.Equal(2015, root.GetProperty("graduationYear").GetInt32());
        Assert.Equal("1992-03-04", root.GetProperty("birthDate").GetString());
        Assert.Equal("Public", root.GetProperty("intakeChannel").GetString());
        Assert.Equal("MIN_EDUCATION", root.GetProperty("eligibilityFlags").GetString());
        Assert.Equal(3, root.GetProperty("documents").GetArrayLength());
        Assert.Equal(2, root.GetProperty("notificationLog").GetArrayLength());
        var offer = Assert.Single(root.GetProperty("offers").EnumerateArray());
        Assert.Equal("Candidate", offer.GetProperty("acceptedBy").GetString());
        Assert.Equal("Accepted", offer.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("otpChallengeCount").GetInt32());
        Assert.Equal(3, root.GetProperty("otpFailedAttemptCount").GetInt32());
        AssertNoSecrets(json);
    }

    [Fact]
    public async Task Candidate_by_recruitment_id_exports_the_same_records()
    {
        await using var db = Hcm030RetentionJobTests.CreateContext();
        var pair = await SeedAsync(db);

        var json = Serialize(await new DsarInternalController(db).GetCandidateByApplication(pair.App.Id, default));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("RecruitmentApplication", root.GetProperty("type").GetString());
        Assert.Equal("Public", root.GetProperty("intakeChannel").GetString());
        Assert.Equal(3, root.GetProperty("documents").GetArrayLength());
        Assert.Equal(2, root.GetProperty("otpChallengeCount").GetInt32());
        Assert.Equal("Candidate", root.GetProperty("offers")[0].GetProperty("acceptedBy").GetString());
        AssertNoSecrets(json);
    }

    [Fact]
    public async Task Candidates_by_hash_carry_the_new_fields_on_both_rows()
    {
        // AcHcm06 collects by hash (the request names only the national ID), so the K2 fields must be there too.
        await using var db = Hcm030RetentionJobTests.CreateContext();
        var pair = await SeedAsync(db);

        var json = Serialize(await new DsarInternalController(db).GetCandidatesByHash(pair.App.NationalIdHash!, default));
        using var doc = JsonDocument.Parse(json);
        var citizen = Assert.Single(doc.RootElement.GetProperty("citizens").EnumerateArray());
        var recruitment = Assert.Single(doc.RootElement.GetProperty("recruitments").EnumerateArray());

        Assert.Equal("تخصص هندسة", citizen.GetProperty("specialization").GetString());
        Assert.Equal(3, citizen.GetProperty("documents").GetArrayLength());
        Assert.Equal(2, citizen.GetProperty("otpChallengeCount").GetInt32());
        Assert.Equal("Public", recruitment.GetProperty("intakeChannel").GetString());
        Assert.Equal("MIN_EDUCATION", recruitment.GetProperty("eligibilityFlags").GetString());
        Assert.Equal(2, recruitment.GetProperty("notificationLog").GetArrayLength());
        var log = recruitment.GetProperty("notificationLog")[0];
        Assert.Equal(PiiMask.Email("samer@example.sy"), log.GetProperty("recipientMasked").GetString());
        Assert.False(string.IsNullOrEmpty(log.GetProperty("templateCode").GetString()));
        Assert.False(string.IsNullOrEmpty(log.GetProperty("status").GetString()));
        Assert.Equal("Candidate", recruitment.GetProperty("offers")[0].GetProperty("acceptedBy").GetString());
        Assert.Equal(2, recruitment.GetProperty("otpChallengeCount").GetInt32());
        AssertNoSecrets(json);
    }

    [Fact]
    public void File_name_digit_runs_are_masked()
    {
        Assert.Equal("cv-*******2233.pdf", DsarInternalController.MaskDigitRuns($"cv-{RawNationalIdInFileName}.pdf"));
        Assert.Equal("cv-2024.pdf", DsarInternalController.MaskDigitRuns("cv-2024.pdf"));
    }

    private static void AssertNoSecrets(string json)
    {
        Assert.DoesNotContain(CodeHmac, json);
        Assert.DoesNotContain(RequestIpHash, json);
        Assert.DoesNotContain("codeHmac", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requestIp", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("نص الرسالة", json);
        Assert.DoesNotContain("\"subject\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("987654", json);
        Assert.DoesNotContain("fileId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RawNationalIdInFileName, json);
        Assert.DoesNotMatch(new Regex(@"\b\d{11}\b"), json);
    }

    private static string Serialize(ActionResult<object> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static async Task<Hcm030RetentionJobTests.Pair> SeedAsync(HRCoreDbContext db)
    {
        var pair = await Hcm030RetentionJobTests.SeedPublicPairAsync(db, "DSAR", closedAt: null);
        pair.App.EligibilityFlags = "MIN_EDUCATION";
        pair.App.EligibilityEvaluatedAt = pair.App.AppliedAt;
        db.CandidateDocuments.Add(new CandidateDocument
        {
            TenantId = Hcm030TestSupport.TenantA,
            RecruitmentApplicationId = pair.App.Id,
            CitizenApplicationId = pair.Citizen.Id,
            DocumentType = "IdCopy",
            FileId = Guid.NewGuid(),
            OriginalFileName = $"هوية-{RawNationalIdInFileName}.pdf",
            ContentType = "application/pdf",
            SizeBytes = 2048,
            UploadedAt = pair.App.AppliedAt
        });
        db.CandidateOtpChallenges.AddRange(
            NewChallenge(pair.Citizen, attempts: 1),
            NewChallenge(pair.Citizen, attempts: 2));
        db.OfferLetters.Add(new OfferLetter
        {
            TenantId = Hcm030TestSupport.TenantA,
            RecruitmentApplicationId = pair.App.Id,
            FundedPositionId = pair.Requisition.FundedPositionId,
            OfferNumber = "OFF-DSAR-1",
            Status = OfferStatus.Accepted,
            AcceptedBy = OfferAcceptedBy.Candidate,
            AcceptedDate = pair.App.AppliedAt.AddDays(20),
            OfferedSalary = 987654m,
            ProposedStartDate = pair.App.AppliedAt.AddDays(40)
        });
        await db.SaveChangesAsync();
        return pair;
    }

    private static CandidateOtpChallenge NewChallenge(CitizenApplication citizen, int attempts) => new()
    {
        TenantId = citizen.TenantId,
        CitizenApplicationId = citizen.Id,
        CodeHmac = CodeHmac,
        Channel = NotificationChannel.Email,
        ExpiresAt = citizen.SubmittedAt.AddMinutes(10),
        Attempts = attempts,
        RequestIp = RequestIpHash
    };
}
