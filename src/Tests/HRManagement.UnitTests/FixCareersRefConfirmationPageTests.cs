using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>
/// FIX-CAREERS-REF: the confirmation page of the public careers portal. The apply hands the citizen's reference number to the page
/// through the flash message only (never through the redirect's URL); the page reads it once, and it promises a message carrying the
/// number only when a candidate notification provider is configured (not <c>Provider=None</c>, the demo's setting).
/// </summary>
public class FixCareersRefConfirmationPageTests
{
    [Fact]
    public async Task A_posting_apply_hands_the_reference_number_to_the_confirmation_page_through_the_flash_message_only()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        await using var context = db;
        var fx = await Hcm030TestSupport.SeedRequisitionAsync(db);
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.Apply(Hcm030TestSupport.ApplyModel(fx.Posting.Id)));

        Assert.Equal(nameof(PublicCareersController.Submitted), redirect.ActionName);
        Assert.True(redirect.RouteValues is null || redirect.RouteValues.Count == 0,
            "The reference number must never go into the confirmation page's URL.");
        var citizen = await db.CitizenApplications.AsNoTracking().SingleAsync();
        Assert.False(string.IsNullOrWhiteSpace(citizen.ReferenceNumber));
        Assert.Equal(citizen.ReferenceNumber, controller.TempData["Reference"]);
    }

    [Theory]
    [InlineData("None", false)]
    [InlineData("", false)]
    [InlineData("Unknown", false)]
    [InlineData("FileDrop", true)]
    [InlineData("Smtp+Sms", true)]
    public void The_confirmation_page_shows_the_number_and_promises_a_message_only_when_a_provider_can_send_it(
        string provider, bool promised)
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        using var context = db;
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);
        controller.TempData["Reference"] = "CA-H30-0001";

        Assert.IsType<ViewResult>(controller.Submitted(Options.Create(new CandidateNotificationOptions { Provider = provider })));

        Assert.Equal("CA-H30-0001", controller.ViewData["Reference"]);
        Assert.Equal(promised, controller.ViewData["CandidateMessagesConfigured"]);
    }

    [Fact]
    public void Without_a_flash_message_the_confirmation_page_has_no_number_to_show()
    {
        var (db, _) = Hcm030TestSupport.CreateContext();
        using var context = db;
        var controller = Hcm030TestSupport.CreatePublicCareersController(db);

        Assert.IsType<ViewResult>(controller.Submitted(Options.Create(new CandidateNotificationOptions())));

        Assert.Null(controller.ViewData["Reference"]);
        Assert.Equal(false, controller.ViewData["CandidateMessagesConfigured"]);
    }
}
