using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-EST-18: MDM admin UI — create grades/classifications, RBAC, deactivate impact.</summary>
public class AcEst18ReferenceCatalogAdminTests
{
    [SkippableFact]
    public async Task Central_creates_GR16_visible_in_grade_lookup()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-18", correlationId);
        recorder.WriteMetadata("AC-EST-18a create grade", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var createPayload = new
        {
            catalogKind = "Grade",
            code = "GR-16",
            nameAr = "الدرجة السادسة عشر",
            nameEn = "Grade 16"
        };
        var createUrl = "api/hr-core/api/reference-data";
        var createResponse = await http.PostAsJsonAsync(createUrl, new
        {
            catalogKind = createPayload.catalogKind,
            code = createPayload.code,
            nameAr = createPayload.nameAr,
            nameEn = createPayload.nameEn
        });
        var createBody = await createResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("create-gr16", "POST", $"/{createUrl}", (int)createResponse.StatusCode, JsonSerializer.Deserialize<object>(createBody));
        Assert.True(createResponse.IsSuccessStatusCode || createResponse.StatusCode == HttpStatusCode.Conflict, createBody);

        var lookupUrl = "api/hr-core/api/lookup/reference-data?kind=Grade&q=GR-16&page=1&pageSize=20";
        var lookupResponse = await http.GetAsync(lookupUrl);
        var lookupBody = await lookupResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("lookup-gr16", "GET", $"/{lookupUrl}", (int)lookupResponse.StatusCode, JsonSerializer.Deserialize<object>(lookupBody));
        Assert.True(lookupResponse.IsSuccessStatusCode, lookupBody);
        using var doc = JsonDocument.Parse(lookupBody);
        Assert.True(doc.RootElement.GetProperty("items").EnumerateArray()
            .Any(i => i.GetProperty("value").GetString() == "GR-16"), lookupBody);

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Central_creates_JcHr_visible_in_classification_lookup()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-18", correlationId);
        recorder.WriteMetadata("AC-EST-18b create job classification", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var createUrl = "api/hr-core/api/reference-data";
        var createResponse = await http.PostAsJsonAsync(createUrl, new
        {
            catalogKind = "JobClassification",
            code = "JC-HR",
            nameAr = "موارد بشرية",
            nameEn = "Human Resources"
        });
        var createBody = await createResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("create-jc-hr", "POST", $"/{createUrl}", (int)createResponse.StatusCode, JsonSerializer.Deserialize<object>(createBody));
        Assert.True(createResponse.IsSuccessStatusCode || createResponse.StatusCode == HttpStatusCode.Conflict, createBody);

        var lookupUrl = "api/hr-core/api/lookup/reference-data?kind=JobClassification&q=JC-HR&page=1&pageSize=20";
        var lookupResponse = await http.GetAsync(lookupUrl);
        var lookupBody = await lookupResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("lookup-jc-hr", "GET", $"/{lookupUrl}", (int)lookupResponse.StatusCode, JsonSerializer.Deserialize<object>(lookupBody));
        Assert.True(lookupResponse.IsSuccessStatusCode, lookupBody);
        using var doc = JsonDocument.Parse(lookupBody);
        Assert.True(doc.RootElement.GetProperty("items").EnumerateArray()
            .Any(i => i.GetProperty("value").GetString() == "JC-HR"), lookupBody);

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Branch_manager_forbidden_on_mvc_create()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("br1-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-18", correlationId);
        recorder.WriteMetadata("AC-EST-18c branch manager forbid create", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId, allowAutoRedirect: false);
        var createPath = "ui/hr-core/ReferenceCatalog/Create?kind=Grade";
        var response = await http.GetAsync(createPath);
        recorder.CaptureHttp("mvc-create-forbidden", "GET", $"/{createPath}", (int)response.StatusCode, new { length = (await response.Content.ReadAsStringAsync()).Length });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        recorder.WriteResult("Pass");
    }

    [SkippableFact]
    public async Task Deactivate_grade_used_in_job_catalog_rejected()
    {
        AcceptanceEnv.RequireEnabled();
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-EST-18", correlationId);
        recorder.WriteMetadata("AC-EST-18d deactivate impact", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId, allowAutoRedirect: false);

        var gradeCode = "G5";
        var byCodeUrl = $"api/hr-core/api/reference-data/by-code?kind=Grade&code={Uri.EscapeDataString(gradeCode)}";
        var byCodeResponse = await http.GetAsync(byCodeUrl);
        var byCodeBody = await byCodeResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("get-grade-g5", "GET", $"/{byCodeUrl}", (int)byCodeResponse.StatusCode, JsonSerializer.Deserialize<object>(byCodeBody));
        Assert.True(byCodeResponse.IsSuccessStatusCode, byCodeBody);

        using var gradeDoc = JsonDocument.Parse(byCodeBody);
        var gradeId = gradeDoc.RootElement.GetProperty("id").GetGuid();

        var impactUrl = $"api/hr-core/api/reference-data/{gradeId:D}/impact";
        var impactResponse = await http.GetAsync(impactUrl);
        var impactBody = await impactResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("impact-g5", "GET", $"/{impactUrl}", (int)impactResponse.StatusCode, JsonSerializer.Deserialize<object>(impactBody));
        Assert.True(impactResponse.IsSuccessStatusCode, impactBody);
        using var impactDoc = JsonDocument.Parse(impactBody);
        Assert.True(impactDoc.RootElement.EnumerateArray().Any(e => e.GetProperty("affectedCount").GetInt32() > 0), impactBody);

        var createPath = "ui/hr-core/ReferenceCatalog/Create?kind=Grade";
        var createPage = await http.GetAsync(createPath);
        var createHtml = await createPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, createPage.StatusCode);
        var tokenMatch = Regex.Match(createHtml, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        Assert.True(tokenMatch.Success, "Antiforgery token missing from create form.");

        var detailsPath = $"ui/hr-core/ReferenceCatalog/Details/{gradeId:D}";
        var detailsResponse = await http.GetAsync(detailsPath);
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        recorder.CaptureHttp("details-g5", "GET", $"/{detailsPath}", (int)detailsResponse.StatusCode, new { length = detailsHtml.Length });
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);

        var deactivatePath = $"ui/hr-core/ReferenceCatalog/Deactivate/{gradeId:D}";
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = tokenMatch.Groups[1].Value
        };
        var deactivateResponse = await http.PostAsync(deactivatePath, new FormUrlEncodedContent(form));
        recorder.CaptureHttp("deactivate-g5", "POST", $"/{deactivatePath}", (int)deactivateResponse.StatusCode, new { location = deactivateResponse.Headers.Location?.ToString() });

        Assert.True(deactivateResponse.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(detailsResponse.Headers.Location ?? detailsResponse.RequestMessage!.RequestUri!)).StatusCode);

        var afterResponse = await http.GetAsync(byCodeUrl);
        var afterBody = await afterResponse.Content.ReadAsStringAsync();
        using var afterDoc = JsonDocument.Parse(afterBody);
        Assert.True(afterDoc.RootElement.GetProperty("isActive").GetBoolean(), "Grade should remain active after rejected deactivate.");

        recorder.WriteDbChecks(new { gradeId, gradeCode, stillActive = true });
        recorder.WriteResult("Pass");
    }
}
