using System.Text.Json;
using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using Compliance.Domain.Services;
using HR.Health.Domain.Entities;
using HR.Health.Domain.Enums;
using HR.Health.Domain.Services;
using HR.Training.Domain.Entities;
using HR.Training.Domain.Enums;
using HR.Training.Domain.Services;

namespace HRManagement.UnitTests;

public class Fun004JourneyMatrixTests
{
    private static string ResolveMatrixPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "fun-htc", "journey-matrix.json");
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("docs/fun-htc/journey-matrix.json");
    }

    [Fact]
    public void Matrix_includes_health_training_compliance()
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(ResolveMatrixPath()));
        var ids = doc.RootElement.GetProperty("journeys").EnumerateArray()
            .Select(j => j.GetProperty("id").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Health", ids);
        Assert.Contains("Training", ids);
        Assert.Contains("Compliance", ids);
    }
}

public class Fun004HealthPrivacyTests
{
    [Fact]
    public void Consent_required_for_clinical_action()
    {
        var record = new MedicalRecord { EmployeeId = Guid.NewGuid() };
        Assert.False(HealthPrivacyRules.HasActiveConsent(record));
        Assert.False(HealthPrivacyRules.CanIssueClinicalAction(record, emergencyOverride: false));
        Assert.True(HealthPrivacyRules.CanIssueClinicalAction(record, emergencyOverride: true));

        record.ConsentGivenAt = DateTime.UtcNow.AddDays(-1);
        record.ConsentPurpose = "فحص دوري";
        record.ConsentExpiresAt = DateTime.UtcNow.AddDays(30);
        Assert.True(HealthPrivacyRules.HasActiveConsent(record));
    }

    [Fact]
    public void SoftCancel_medical_blocks_purge_until_retention()
    {
        var record = new MedicalRecord { EmployeeId = Guid.NewGuid() };
        HealthPrivacyRules.SoftCancel(record, "اختبار");
        Assert.True(record.IsCancelled);
        Assert.False(HealthPrivacyRules.CanPurgeAfterRetention(record));
        Assert.True(HealthPrivacyRules.CanPurgeAfterRetention(record, record.CancelledAt!.Value.AddDays(3651)));
    }

    [Fact]
    public void Insurance_lifecycle_transitions()
    {
        var card = new InsuranceCard
        {
            EmployeeId = Guid.NewGuid(),
            MemberNumber = "M1",
            IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            Status = InsuranceCardStatus.Draft
        };
        Assert.True(InsuranceLifecycleRules.CanActivate(card));
        card.Status = InsuranceCardStatus.Active;
        Assert.True(InsuranceLifecycleRules.CanSuspend(card));
        Assert.True(InsuranceLifecycleRules.CanRevoke(card));
    }
}

public class Fun004TrainingCertTests
{
    [Fact]
    public void Checklist_precomplete_requires_enrolled_evidence()
    {
        var id = Guid.NewGuid();
        var items = TrainingChecklistDefaults.CreateFor(id).ToList();
        Assert.False(TrainingChecklistDefaults.PreCompleteGate(items));
        TrainingChecklistDefaults.Mark(items.First(i => i.Step == TrainingChecklistStep.Enrolled), null, "e1");
        Assert.True(TrainingChecklistDefaults.PreCompleteGate(items));
    }

    [Fact]
    public void Certificate_number_and_verify_format()
    {
        var no = LearningCompletionRules.GenerateCertificateNumber("ABC", Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));
        Assert.True(CertificateVerificationRules.IsValidFormat(no));
        Assert.False(CertificateVerificationRules.IsValidFormat("bad"));
    }
}

public class Fun004ComplianceProtectionTests
{
    [Fact]
    public void Anonymous_hides_reporter_from_non_investigator()
    {
        var c = new Complaint
        {
            FiledBy = "secret",
            Subject = "x",
            IsAnonymous = true,
            AssignedTo = Guid.NewGuid(),
            ReporterProtectedId = Guid.NewGuid()
        };
        Assert.False(ComplaintProtectionRules.CanSeeReporter(c, Guid.NewGuid(), isInvestigatorRole: false));
        Assert.True(ComplaintProtectionRules.CanSeeReporter(c, c.AssignedTo, isInvestigatorRole: true));
    }

    [Fact]
    public void Cannot_assign_reporter_as_investigator()
    {
        var reporter = Guid.NewGuid();
        var c = new Complaint
        {
            FiledBy = "a",
            Subject = "s",
            ReporterProtectedId = reporter
        };
        Assert.False(ComplaintProtectionRules.CanAssignInvestigator(c, reporter));
        Assert.True(ComplaintProtectionRules.CanAssignInvestigator(c, Guid.NewGuid()));
    }

    [Fact]
    public void CanIssue_blocks_pending_appeal()
    {
        Assert.True(DisciplinaryWorkflowRules.CanIssue(AppealStatus.None));
        Assert.False(DisciplinaryWorkflowRules.CanIssue(AppealStatus.Pending));
    }
}
