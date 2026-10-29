using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>COM-030 (PLAN-30 F2): contact and status-notification consent on the public apply form.</summary>
public class Hcm030ApplyContactConsentTests
{
    [Fact]
    public async Task Linked_posting_requires_a_phone_or_an_email()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var vm = Hcm030TestSupport.ApplyModel(fx.Posting.Id);
        vm.Phone = " ";
        vm.Email = null;
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);

        var result = await controller.Apply(vm);

        Assert.Equal("Details", Assert.IsType<ViewResult>(result).ViewName);
        Assert.True(controller.ModelState.ContainsKey(nameof(vm.Phone)));
        Assert.Empty(await db.CitizenApplications.ToListAsync());
    }

    [Theory]
    [InlineData("099", null, "Phone")]
    [InlineData("09990000001234567", null, "Phone")]
    [InlineData("abc-def", null, "Phone")]
    [InlineData(null, "not-an-email", "Email")]
    [InlineData(null, "a@b", "Email")]
    [InlineData(null, "a b@test.sy", "Email")]
    public async Task Invalid_contact_formats_are_refused(string? phone, string? email, string field)
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var vm = Hcm030TestSupport.ApplyModel(fx.Posting.Id);
        vm.Phone = phone;
        vm.Email = email;
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);

        Assert.IsType<ViewResult>(await controller.Apply(vm));

        Assert.True(controller.ModelState.ContainsKey(field));
        Assert.Empty(await db.CitizenApplications.ToListAsync());
    }

    [Fact]
    public async Task Phone_is_stored_in_E164_and_consent_with_sms_is_recorded()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var vm = Hcm030TestSupport.ApplyModel(fx.Posting.Id);
        vm.Phone = "0999 123-456";
        vm.Email = "  c@test.sy ";
        vm.PreferredChannel = NotificationChannel.Sms;
        vm.NotificationConsent = true;
        var before = DateTime.UtcNow;

        var result = await Hcm030TestSupport.CreatePublicCareersController(db).Apply(vm);

        Assert.IsType<RedirectToActionResult>(result);
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync();
        Assert.Equal("+963999123456", citizen.Phone);
        Assert.Equal("c@test.sy", citizen.Email);
        Assert.Equal(NotificationChannel.Sms, citizen.NotificationChannel);
        Assert.NotNull(citizen.NotificationConsentAt);
        Assert.True(citizen.NotificationConsentAt >= before.AddSeconds(-1));
    }

    [Fact]
    public async Task No_consent_means_no_channel_and_no_consent_date()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var vm = Hcm030TestSupport.ApplyModel(fx.Posting.Id);
        vm.PreferredChannel = NotificationChannel.Email;
        vm.NotificationConsent = false;

        Assert.IsType<RedirectToActionResult>(await Hcm030TestSupport.CreatePublicCareersController(db).Apply(vm));

        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync();
        Assert.Null(citizen.NotificationChannel);
        Assert.Null(citizen.NotificationConsentAt);
        Assert.Equal("+963999000000", citizen.Phone);
    }

    [Theory]
    [InlineData(null, "0999000000", "c@test.sy")]
    [InlineData(NotificationChannel.Email, "0999000000", null)]
    [InlineData(NotificationChannel.Sms, null, "c@test.sy")]
    [InlineData(NotificationChannel.Push, "0999000000", "c@test.sy")]
    [InlineData(NotificationChannel.InApp, "0999000000", "c@test.sy")]
    public async Task Consent_needs_a_valid_channel_with_its_contact(NotificationChannel? channel, string? phone, string? email)
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var vm = Hcm030TestSupport.ApplyModel(fx.Posting.Id);
        vm.Phone = phone;
        vm.Email = email;
        vm.PreferredChannel = channel;
        vm.NotificationConsent = true;
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);

        Assert.IsType<ViewResult>(await controller.Apply(vm));

        Assert.True(controller.ModelState.ContainsKey(nameof(vm.PreferredChannel)));
        Assert.Empty(await db.CitizenApplications.ToListAsync());
    }

    [Fact]
    public async Task Legacy_posting_requires_a_phone_or_an_email()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var posting = new PublicJobPosting { TenantId = Hcm030TestSupport.TenantA, Title = "قديم", IsPublished = true };
        db.PublicJobPostings.Add(posting);
        await db.SaveChangesAsync();
        var vm = Hcm030TestSupport.ApplyModel(posting.Id);
        vm.Phone = null;
        vm.Email = " ";
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);

        var result = await controller.Apply(vm);

        Assert.Equal("Details", Assert.IsType<ViewResult>(result).ViewName);
        Assert.True(controller.ModelState.ContainsKey(nameof(vm.Phone)));
        Assert.Empty(await db.CitizenApplications.ToListAsync());
    }

    [Fact]
    public async Task Legacy_posting_ignores_consent()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var posting = new PublicJobPosting { TenantId = Hcm030TestSupport.TenantA, Title = "قديم", IsPublished = true };
        db.PublicJobPostings.Add(posting);
        await db.SaveChangesAsync();
        var vm = Hcm030TestSupport.ApplyModel(posting.Id);
        vm.Phone = null;
        vm.PreferredChannel = NotificationChannel.Sms;
        vm.NotificationConsent = true;

        Assert.IsType<RedirectToActionResult>(await Hcm030TestSupport.CreatePublicCareersController(db).Apply(vm));

        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync();
        Assert.Equal("c@test.sy", citizen.Email);
        Assert.Null(citizen.NotificationChannel);
        Assert.Null(citizen.NotificationConsentAt);
    }

    [Fact]
    public async Task Country_code_comes_from_the_portal_option()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var __ = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var vm = Hcm030TestSupport.ApplyModel(fx.Posting.Id);
        vm.Phone = "07700900123";
        var options = new RecruitmentOptions { Portal = { PhoneCountryCode = "44" } };

        Assert.IsType<RedirectToActionResult>(
            await Hcm030TestSupport.CreatePublicCareersController(db, options: options).Apply(vm));

        Assert.Equal("+447700900123", (await db.CitizenApplications.AsNoTracking().SingleAsync()).Phone);
    }

    [Theory]
    [InlineData("0999000000", "+963999000000")]
    [InlineData("+963 999 000 000", "+963999000000")]
    [InlineData("00963999000000", "+963999000000")]
    [InlineData("963999000000", "+963999000000")]
    [InlineData("999000000", "+963999000000")]
    [InlineData("(011) 222-3344", "+963112223344")]
    [InlineData("٠٩٩٩٠٠٠٠٠٠", "+963999000000")]
    [InlineData("+44 7700 900123", "+447700900123")]
    public void Phone_normalization(string raw, string expected)
    {
        Assert.True(CandidateContactRules.TryNormalizePhone(raw, "963", out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("099")]
    [InlineData("+0999000000")]
    [InlineData("+1234567890123456")]
    [InlineData("0999-abc-000")]
    [InlineData("+963 12")]
    public void Phone_normalization_rejects(string raw) =>
        Assert.False(CandidateContactRules.TryNormalizePhone(raw, "963", out _));

    [Theory]
    [InlineData("c@test.sy", true)]
    [InlineData("first.last+tag@sub.example.gov.sy", true)]
    [InlineData("c@test", false)]
    [InlineData("c@@test.sy", false)]
    [InlineData("@test.sy", false)]
    [InlineData("c@.test.sy", false)]
    [InlineData("Name <c@test.sy>", false)]
    public void Email_validation(string value, bool valid) =>
        Assert.Equal(valid, CandidateContactRules.IsValidEmail(value));
}
