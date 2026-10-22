using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;
using R = HR.Core.Domain.Services.RecruitmentEligibilityRules;

namespace HRManagement.UnitTests;

/// <summary>HCM-030 (D, G-17): eligibility flags at intake — flags only, and an audited reason to shortlist.</summary>
public class RecruitmentEligibilityRulesTests
{
    private static readonly DateOnly AppliedOn = new(2026, 10, 23);
    private static readonly EligibilityCriteria None = new(null, null, null, null);

    private static IReadOnlyList<string> Evaluate(EligibilityProfile profile, EligibilityCriteria criteria) =>
        R.Evaluate(profile, criteria, AppliedOn);

    [Theory]
    [InlineData("DIP", "BACH", true)]
    [InlineData("BACH", "BACH", false)]
    [InlineData("PHD", "MASTER", false)]
    [InlineData("bach", "MASTER", true)]
    public void Qualification_below_the_minimum_is_flagged(string held, string minimum, bool flagged)
    {
        var flags = Evaluate(new EligibilityProfile(held, null, null), None with { MinQualificationCode = minimum });

        Assert.Equal(flagged, flags.Contains(R.BelowMinQualification));
    }

    [Theory]
    [InlineData("VOC", "BACH")]
    [InlineData("BACH", "VOC")]
    public void Unranked_qualification_is_flagged_and_never_compared(string held, string minimum)
    {
        var flags = Evaluate(new EligibilityProfile(held, null, null), None with { MinQualificationCode = minimum });

        Assert.Equal([R.QualificationUnranked], flags);
    }

    [Fact]
    public void Configured_ranks_replace_the_defaults()
    {
        var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["VOC"] = 1, ["BACH"] = 2 };

        var flags = R.Evaluate(new EligibilityProfile("VOC", null, null), None with { MinQualificationCode = "BACH" }, AppliedOn, ranks);

        Assert.Equal([R.BelowMinQualification], flags);
    }

    [Theory]
    [InlineData(1.5, 2, true)]
    [InlineData(2, 2, false)]
    [InlineData(10, 2, false)]
    public void Experience_below_the_minimum_is_flagged(double years, double minimum, bool flagged)
    {
        var flags = Evaluate(new EligibilityProfile(null, (decimal)years, null), None with { MinYearsOfExperience = (decimal)minimum });

        Assert.Equal(flagged, flags.Contains(R.BelowMinExperience));
    }

    [Theory]
    // Age on the application date; a birthday counts on the day itself.
    [InlineData("2008-10-23", 18, null, null)]
    [InlineData("2008-10-24", 18, null, R.AgeBelowMin)]
    [InlineData("1991-10-23", null, 35, null)]
    [InlineData("1990-10-23", null, 35, R.AgeAboveMax)]
    [InlineData("1990-10-24", null, 35, null)]
    [InlineData("2008-02-29", 18, 18, null)]
    public void Age_bounds_are_inclusive_on_the_application_date(string birthDate, int? minAge, int? maxAge, string? expected)
    {
        var flags = Evaluate(new EligibilityProfile(null, null, DateOnly.Parse(birthDate)), None with { MinAge = minAge, MaxAge = maxAge });

        Assert.Equal(expected is null ? [] : [expected], flags);
    }

    [Fact]
    public void Missing_profile_data_is_flagged_once()
    {
        var flags = Evaluate(new EligibilityProfile(null, null, null), new EligibilityCriteria("BACH", 3, 18, 40));

        Assert.Equal([R.MissingProfileData], flags);
    }

    [Fact]
    public void Null_criteria_check_nothing()
    {
        Assert.Empty(Evaluate(new EligibilityProfile(null, null, null), None));
        Assert.Empty(Evaluate(new EligibilityProfile("DIP", 0, new DateOnly(2010, 1, 1)), None));
        Assert.True(None.IsEmpty);
    }

    [Fact]
    public void Several_flags_keep_the_display_order_and_round_trip()
    {
        var flags = Evaluate(new EligibilityProfile("DIP", 1, null), new EligibilityCriteria("BACH", 3, 18, null));

        Assert.Equal([R.BelowMinQualification, R.BelowMinExperience, R.MissingProfileData], flags);
        Assert.Equal(flags, R.Parse(R.Format(flags)));
        Assert.Null(R.Format([]));
        Assert.Equal("دون المؤهل المطلوب", R.LabelAr(R.BelowMinQualification));
        Assert.Equal(6, R.Flags.Count);
    }

    [Fact]
    public void Highest_ranked_qualification_ignores_unranked_codes()
    {
        Assert.Equal("MASTER", R.HighestRanked(["BACH", "MASTER", null, "VOC", "DIP"]));
        Assert.Null(R.HighestRanked(["VOC", null]));
    }

    [Fact]
    public async Task Public_apply_stores_the_profile_snapshot_and_its_flags()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        fx.Requisition.MinQualificationCode = "BACH";
        fx.Requisition.MinYearsOfExperience = 3;
        fx.Requisition.MaxAge = 35;
        await db.SaveChangesAsync();
        await SeedQualificationsAsync(db);
        var vm = Hcm030TestSupport.ApplyModel(fx.Posting.Id);
        vm.EducationLevelCode = "dip";
        vm.Specialization = "محاسبة";
        vm.GraduationYear = 2015;
        vm.YearsOfExperience = 5;
        vm.BirthDate = new DateOnly(1985, 3, 1);

        await Hcm030TestSupport.CreatePublicCareersController(db).Apply(vm);

        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync();
        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync();
        Assert.Equal("DIP", citizen.EducationLevelCode);
        Assert.Equal(new DateOnly(1985, 3, 1), citizen.BirthDate);
        Assert.Equal("DIP", app.EducationLevelCode);
        Assert.Equal("محاسبة", app.Specialization);
        Assert.Equal(2015, app.GraduationYear);
        Assert.Equal(5, app.YearsOfExperience);
        Assert.Equal(new DateOnly(1985, 3, 1), app.BirthDate);
        Assert.Equal($"{R.BelowMinQualification},{R.AgeAboveMax}", app.EligibilityFlags);
        Assert.NotNull(app.EligibilityEvaluatedAt);
        // Flags never reject: the application is open for screening.
        Assert.Equal(RecruitmentApplicationStatus.Submitted, app.Status);
    }

    [Fact]
    public async Task Public_apply_refuses_an_unknown_qualification_code_and_an_implausible_birth_date()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        await SeedQualificationsAsync(db);
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);
        var vm = Hcm030TestSupport.ApplyModel(fx.Posting.Id);
        vm.EducationLevelCode = "XYZ";
        vm.BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-5);

        await controller.Apply(vm);

        Assert.True(controller.ModelState.ContainsKey(nameof(vm.EducationLevelCode)));
        Assert.True(controller.ModelState.ContainsKey(nameof(vm.BirthDate)));
        Assert.Empty(await db.CitizenApplications.ToListAsync());
    }

    [Fact]
    public async Task Internal_apply_snapshots_birth_date_and_the_highest_ranked_qualification()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        fx.Requisition.MinQualificationCode = "MASTER";
        var employee = await Hcm030TestSupport.SeedEmployeeAsync(db);
        db.Qualifications.AddRange(
            new Qualification { TenantId = Hcm030TestSupport.TenantA, EmployeeId = employee.Id, Title = "إجازة", QualificationCode = "BACH" },
            new Qualification { TenantId = Hcm030TestSupport.TenantA, EmployeeId = employee.Id, Title = "دورة", QualificationCode = "VOC" },
            new Qualification { TenantId = Hcm030TestSupport.TenantA, EmployeeId = employee.Id, Title = "دبلوم", QualificationCode = "DIP" });
        await db.SaveChangesAsync();

        await Hcm030TestSupport.CreateEssController(db, employee.Id).Apply(fx.Vacancy.Id, null);

        var app = await db.RecruitmentApplications.AsNoTracking().SingleAsync();
        Assert.Equal("BACH", app.EducationLevelCode);
        Assert.Equal(new DateOnly(1990, 1, 1), app.BirthDate);
        Assert.Equal(R.BelowMinQualification, app.EligibilityFlags);
    }

    [Fact]
    public void Age_bounds_by_regime_apply_only_when_the_requisition_sets_none()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        using var __ = db;
        var options = new RecruitmentOptions();
        options.Eligibility.AgeBoundsByRegime["law50"] = new RecruitmentAgeBounds { MinAge = 18, MaxAge = 40 };
        var service = new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(options));
        var requisition = new RecruitmentRequisition { LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004 };

        Assert.Equal(new EligibilityCriteria(null, null, 18, 40), service.CriteriaFor(requisition));
        requisition.MaxAge = 30;
        Assert.Equal(new EligibilityCriteria(null, null, 18, 30), service.CriteriaFor(requisition));
        Assert.True(new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()))
            .CriteriaFor(new RecruitmentRequisition { LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004 }).IsEmpty);
    }

    [Fact]
    public async Task Shortlisting_a_flagged_application_requires_a_reason_that_is_stored_and_audited()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var app = new RecruitmentApplication
        {
            TenantId = Hcm030TestSupport.TenantA,
            ApplicantFullName = "مرشح",
            RequisitionId = fx.Requisition.Id,
            AppliedPosition = "محلل نظم",
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.UnderReview,
            ConflictOfInterestDeclared = true,
            BirthDate = new DateOnly(1980, 1, 1),
            EligibilityFlags = $"{R.BelowMinExperience},{R.AgeAboveMax}"
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();
        var selection = new RecruitmentSelectionService(db);
        var actor = Guid.NewGuid();

        var (noReason, noReasonError) = await selection.TransitionStatusAsync(app.Id, RecruitmentApplicationStatus.Shortlisted, false);
        var (shortReason, _) = await selection.TransitionStatusAsync(
            app.Id, RecruitmentApplicationStatus.Shortlisted, false, eligibilityOverrideReason: "  قصير  ");
        var (ok, okError) = await selection.TransitionStatusAsync(
            app.Id, RecruitmentApplicationStatus.Shortlisted, false,
            eligibilityOverrideReason: "  خبرة نوعية تعوّض الفارق بقرار اللجنة  ", actorUserId: actor);

        Assert.False(noReason);
        Assert.Equal(R.OverrideReasonRequiredMessage, noReasonError);
        Assert.False(shortReason);
        Assert.True(ok, okError);
        var saved = await db.RecruitmentApplications.AsNoTracking().SingleAsync();
        Assert.Equal(RecruitmentApplicationStatus.Shortlisted, saved.Status);
        Assert.Equal("خبرة نوعية تعوّض الفارق بقرار اللجنة", saved.EligibilityOverrideReason);
        var audit = Assert.Single(await db.AuditLogs.AsNoTracking().Where(a => a.Action == R.OverrideAuditAction).ToListAsync());
        Assert.Equal(app.Id, audit.EntityId);
        Assert.Equal(actor, audit.UserId);
        var fields = System.Text.Json.JsonDocument.Parse(audit.ChangedFields!).RootElement;
        Assert.Equal([R.BelowMinExperience, R.AgeAboveMax], fields.GetProperty("flags").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal("خبرة نوعية تعوّض الفارق بقرار اللجنة", fields.GetProperty("reason").GetString());
        // Flags and reason only — no birth date or other profile data in the audit.
        Assert.Equal(["flags", "reason"], fields.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Unflagged_application_is_shortlisted_without_a_reason()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var app = new RecruitmentApplication
        {
            TenantId = Hcm030TestSupport.TenantA,
            ApplicantFullName = "مرشح",
            RequisitionId = fx.Requisition.Id,
            AppliedPosition = "محلل نظم",
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.UnderReview,
            ConflictOfInterestDeclared = true
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();

        var (ok, error) = await new RecruitmentSelectionService(db)
            .TransitionStatusAsync(app.Id, RecruitmentApplicationStatus.Shortlisted, false);

        Assert.True(ok, error);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == R.OverrideAuditAction).ToListAsync());
    }

    private static async Task SeedQualificationsAsync(HR.Core.Infrastructure.Data.HRCoreDbContext db)
    {
        foreach (var (code, name) in new[] { ("DIP", "دبلوم"), ("BACH", "بكالوريوس"), ("MASTER", "ماجستير"), ("PHD", "دكتوراه") })
        {
            db.ReferenceCatalogEntries.Add(new HRManagement.SharedKernel.MasterData.ReferenceCatalogEntry
            {
                TenantId = HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId,
                CatalogKind = HRManagement.SharedKernel.MasterData.CatalogKind.Qualification,
                Code = code,
                NameAr = name
            });
        }
        await db.SaveChangesAsync();
    }
}
