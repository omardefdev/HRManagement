using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-31: hidden admin fields in logistics create form are not persisted.</summary>
public class Ac31MassAssignmentTests
{
    [SkippableFact]
    public async Task Inventory_create_ignores_hidden_tenant_and_audit_fields()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("a-logistics-maker");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-31", correlationId);
        recorder.WriteMetadata("AC-31 mass assignment protection", persona.Id);
        recorder.WriteSteps("""
            1. Open the inventory create form as the Agency A logistics maker.
            2. Submit valid business fields plus forged Id, TenantId, CreatedAt, and ApprovedBy fields.
            3. Locate the created row in Agency A and prove its server-generated Id differs from the forged Id.
            4. Search for the same unique row as Agency B and prove tenant isolation hides it.
            """);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(
            persona, correlationId, allowAutoRedirect: false);
        var createPath = "ui/admin-logistics/InventoryItems/Create";
        var getForm = await http.GetAsync(createPath);
        var html = await getForm.Content.ReadAsStringAsync();
        recorder.CaptureHttp("get-create-form", "GET", $"/{createPath}", (int)getForm.StatusCode,
            new { length = html.Length });

        Assert.Equal(HttpStatusCode.OK, getForm.StatusCode);
        var tokenMatch = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        Assert.True(tokenMatch.Success, "Antiforgery token missing from create form.");

        var evilTenant = Guid.Parse("aaaaaaaa-aaaa-4000-8000-000000000099");
        var evilId = Guid.NewGuid();
        var probeName = $"AC31-{evilId:N}";
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenMatch.Groups[1].Value,
            ["Name"] = probeName,
            ["Quantity"] = "1",
            ["Unit"] = "ea",
            ["Location"] = "AC31",
            ["ReorderThreshold"] = "1",
            ["TenantId"] = evilTenant.ToString("D"),
            ["Id"] = evilId.ToString("D"),
            ["CreatedAt"] = DateTime.UtcNow.AddYears(-5).ToString("O"),
            ["ApprovedBy"] = Guid.NewGuid().ToString("D")
        };

        var post = await http.PostAsync(createPath, new FormUrlEncodedContent(form));
        var postBody = await post.Content.ReadAsStringAsync();
        recorder.CaptureHttp("post-with-hidden-fields", "POST", $"/{createPath}",
            (int)post.StatusCode, new { bodyLength = postBody.Length, location = post.Headers.Location?.ToString() });

        Assert.True(post.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"Unexpected POST status: {post.StatusCode}");

        var agencyAPath = $"ui/admin-logistics/InventoryItems?q={Uri.EscapeDataString(probeName)}&page=1&pageSize=20";
        var agencyAResponse = await http.GetAsync(agencyAPath);
        var agencyAHtml = await agencyAResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("agency-a-search", "GET", $"/{agencyAPath}",
            (int)agencyAResponse.StatusCode, new { length = agencyAHtml.Length, probeName });
        Assert.Equal(HttpStatusCode.OK, agencyAResponse.StatusCode);
        var resultCell = new Regex(
            $@"<td>\s*{Regex.Escape(probeName)}\s*</td>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(resultCell.IsMatch(agencyAHtml), "Agency A search did not render the created row.");

        var persisted = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(
            "admin_db",
            $"SELECT \"Id\"::text || '|' || \"TenantId\"::text FROM \"InventoryItems\" WHERE \"Name\" = '{probeName}' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        var persistedParts = persisted.Split('|');
        Assert.Equal(2, persistedParts.Length);
        var actualId = Guid.Parse(persistedParts[0]);
        var actualTenant = Guid.Parse(persistedParts[1]);
        Assert.NotEqual(evilId, actualId);
        Assert.Equal(AcceptanceSeedIds.AgencyAId, actualTenant);

        var agencyB = AcceptanceHarness.RequirePersona("b-logistics");
        using var agencyBHttp = await AcceptanceHarness.CreateAuthorizedClientAsync(agencyB, correlationId);
        var agencyBResponse = await agencyBHttp.GetAsync(agencyAPath);
        var agencyBHtml = await agencyBResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("agency-b-search", "GET", $"/{agencyAPath}",
            (int)agencyBResponse.StatusCode, new { length = agencyBHtml.Length, probeName });
        Assert.Equal(HttpStatusCode.OK, agencyBResponse.StatusCode);
        Assert.False(resultCell.IsMatch(agencyBHtml), "Agency B rendered Agency A's inventory row.");

        recorder.WriteDbChecks(new
        {
            expectedTenant = AcceptanceSeedIds.AgencyAId,
            rejectedTenant = evilTenant,
            forgedId = evilId,
            persistedId = actualId,
            persistedTenant = actualTenant,
            agencyAVisible = true,
            agencyBVisible = false
        });
        recorder.WriteAfter(new { postStatus = (int)post.StatusCode, evilTenant, evilId, actualId, probeName });
        recorder.WriteResult("Pass");
    }
}
