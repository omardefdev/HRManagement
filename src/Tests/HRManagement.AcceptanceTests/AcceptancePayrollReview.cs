using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-58 (§4, D-PAY-3, D-PAY-5): what a posting needs since PLAN-58, through the gateway and by the people who own each act. Every
/// approval the run's amount asks for — above the agency's threshold the second is <c>acc-a-payroll-approver-2</c>'s — and the
/// independent review: <c>acc-auditor</c> opens it and <c>acc-a-auditor-2</c> closes it, each from his own session. No SQL fixture
/// remains (the fixture script was deleted with PLAN-58).
/// </summary>
internal static class AcceptancePayrollReview
{
    public const string FirstAuditor = "auditor";
    public const string SecondAuditor = "a-auditor-2";
    public const string SecondApprover = "a-payroll-approver-2";

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Text)> PostAsync(
        HttpClient client, EvidenceRecorder recorder, string step, string path, object? body)
    {
        using var response = await client.PostAsJsonAsync(path, body ?? new { });
        var text = await response.Content.ReadAsStringAsync();
        JsonElement json = default;
        if (text.TrimStart().StartsWith('{'))
            json = JsonSerializer.Deserialize<JsonElement>(text);
        recorder.CaptureHttp(step, "POST", "/" + path, (int)response.StatusCode,
            json.ValueKind == JsonValueKind.Undefined ? (text.Length > 300 ? text[..300] : text) : json);
        return (response.StatusCode, json, text);
    }

    private static string Error(JsonElement body) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";

    /// <summary>
    /// The independent review of a run under review or approved: opened by one auditor, closed by another. Returns what the evidence keeps
    /// (the record's id and both answers' statuses).
    /// </summary>
    public static async Task<object> ReviewAsync(EvidenceRecorder recorder, string runId, string tag)
    {
        using var opener = await AcceptanceHarness.CreatePayrollAuditClientAsync(AcceptanceHarness.RequirePersona(FirstAuditor), Guid.NewGuid().ToString("D"));
        var (opened, openedBody, openedText) = await PostAsync(opener, recorder, $"{tag}-review-open-{FirstAuditor}",
            $"api/payroll/runs/{runId}/audit-records", new { findings = $"مراجعة مستقلة — {tag}" });
        Assert.True(opened == HttpStatusCode.OK, $"{tag}: the review's opening answered {(int)opened}: {Error(openedBody)} {(openedText.Length > 200 ? openedText[..200] : openedText)}");
        var recordId = openedBody.GetProperty("recordId").GetGuid();

        using var closer = await AcceptanceHarness.CreatePayrollAuditClientAsync(AcceptanceHarness.RequirePersona(SecondAuditor), Guid.NewGuid().ToString("D"));
        var (closed, closedBody, _) = await PostAsync(closer, recorder, $"{tag}-review-close-{SecondAuditor}",
            $"api/payroll/audit-records/{recordId:D}/close", null);
        Assert.True(closed == HttpStatusCode.OK, $"{tag}: the review's closing answered {(int)closed}: {Error(closedBody)}");
        return new { review = new { recordId, openedBy = FirstAuditor, closedBy = SecondAuditor, opened = (int)opened, closed = (int)closed } };
    }

    /// <summary>
    /// After the first approval's answer: when the run still waits for an approval (above the threshold, «اعتماد 1 من 2»), the second
    /// approver approves it from his own session. Returns the approvals given and required, as the evidence keeps them.
    /// </summary>
    public static async Task<object> CompleteApprovalAsync(EvidenceRecorder recorder, string runId, JsonElement firstAnswer, string tag)
    {
        var given = firstAnswer.TryGetProperty("approvalsGiven", out var g) ? g.GetInt32() : 1;
        var required = firstAnswer.TryGetProperty("approvalsRequired", out var r) ? r.GetInt32() : 1;
        if (given >= required)
            return new { approvalsGiven = given, approvalsRequired = required };

        using var second = await AcceptanceHarness.CreatePayrollClientAsync(AcceptanceHarness.RequirePersona(SecondApprover), Guid.NewGuid().ToString("D"));
        var (approved, approvedBody, _) = await PostAsync(second, recorder, $"{tag}-approve-{SecondApprover}", $"api/payroll/runs/{runId}/approve", null);
        Assert.True(approved == HttpStatusCode.OK, $"{tag}: the second approval answered {(int)approved}: {Error(approvedBody)}");
        return new
        {
            approvalsGiven = approvedBody.GetProperty("approvalsGiven").GetInt32(),
            approvalsRequired = approvedBody.GetProperty("approvalsRequired").GetInt32(),
            secondApprover = SecondApprover,
        };
    }
}

/// <summary>
/// PLAN-58 (§6 rule 3): no acceptance seed gives an employee a bank account, and an order with no pinned account is held
/// <c>NO-ACCOUNT</c>. A class that sends a batch first gives a few of its own employees a verified account by PLAN-55's two people
/// (the processor registers, the approver verifies), before its run is calculated (the snapshot pins it). The numbers are test IBANs
/// whose check digits are computed here, made of the class's mark and the employee's id: never a real account, never shared by accident.
/// </summary>
internal static class AcceptancePayrollAccounts
{
    /// <summary>A test IBAN: «SY», check digits (ISO 7064 mod 97-10, computed independently of the product), the class's mark and the id.</summary>
    public static string Iban(string employee, string mark)
    {
        var bban = (mark + employee.Replace("-", "", StringComparison.Ordinal)).ToUpperInvariant()[..24];
        var check = 98 - Mod97(bban + "SY00");
        return "SY" + check.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + bban;
    }

    private static int Mod97(string text)
    {
        var remainder = 0;
        foreach (var c in text)
            remainder = char.IsDigit(c) ? ((remainder * 10) + (c - '0')) % 97 : ((remainder * 100) + (c - 'A' + 10)) % 97;
        return remainder;
    }

    /// <summary>
    /// Agency A's employees with an active pay profile in a unit, no bank account and no salary change request, from the highest ids
    /// (AC-PAY-55 picks from the lowest), skipping <paramref name="skip"/> of them. <paramref name="lowest"/> picks from the lowest ids
    /// instead: a partial funding pays the batch's orders in the order of their employee ids, so a funded employee is a low one.
    /// </summary>
    public static async Task<string[]> PickWithoutAccountAsync(EvidenceRecorder recorder, string step, int count, int skip = 0, bool lowest = false)
    {
        var direction = lowest ? "ASC" : "DESC";
        var tenant = AcceptanceSeedIds.AgencyAId.ToString("D");
        var seeded = string.Join(",", new[] { AcceptanceSeedIds.EmpBr1Id, AcceptanceSeedIds.EmpBr2Id }.Select(id => $"'{id:D}'"));
        var rows = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync("payroll_db",
            $"SELECT COALESCE(string_agg(e, ',' ORDER BY e {direction}), '') FROM (SELECT p.\"EmployeeId\"::text AS e FROM \"EmployeePayProfiles\" p " +
            $"WHERE p.\"TenantId\"='{tenant}' AND p.\"IsActive\" AND p.\"OrgUnitId\" IS NOT NULL AND p.\"EmployeeId\" NOT IN ({seeded}) " +
            "AND NOT EXISTS (SELECT 1 FROM \"EmployeeBankAccounts\" a WHERE a.\"EmployeeId\" = p.\"EmployeeId\") " +
            "AND NOT EXISTS (SELECT 1 FROM \"PayProfileChangeRequests\" r WHERE r.\"EmployeeId\" = p.\"EmployeeId\") " +
            $"ORDER BY p.\"EmployeeId\" {direction} OFFSET {skip} LIMIT {count}) picked;");
        var picked = rows.Split(',', StringSplitOptions.RemoveEmptyEntries);
        AcceptanceGate.RequireData(recorder, picked.Length == count,
            $"{step}: لا يوجد في الجهة A {count} موظفين بملف أجر فعّال وبلا حساب مصرفي وبلا طلب تغيير راتب",
            $"{step}: agency A has no {count} employees with an active pay profile, no bank account and no salary change request.");
        return picked;
    }

    /// <summary>The processor registers <paramref name="iban"/> for the employee; the approver, another person, verifies it. Returns the account's id.</summary>
    public static async Task<string> GiveVerifiedAccountAsync(
        EvidenceRecorder recorder, HttpClient processor, HttpClient approver, string employee, string iban, string reference, string step)
    {
        var accounts = $"api/payroll/employees/{employee}/bank-accounts";
        using var registered = await processor.PostAsJsonAsync(accounts, new
        {
            accountKind = "Iban",
            accountNumber = iban,
            bankNameAr = "المصرف التجاري السوري",
            holderNameAr = "موظف اختبار القبول",
        });
        var registeredText = await registered.Content.ReadAsStringAsync();
        // The answer masks the number; the evidence keeps the answer only.
        recorder.CaptureHttp($"{step}-register-{employee[..8]}", "POST", "/" + accounts, (int)registered.StatusCode, JsonSerializer.Deserialize<JsonElement>(registeredText));
        Assert.True(registered.StatusCode == HttpStatusCode.Created, $"{step}: registering an account for {employee} answered {(int)registered.StatusCode}: {registeredText}");
        var accountId = JsonSerializer.Deserialize<JsonElement>(registeredText).GetProperty("accountId").GetGuid().ToString("D");

        using var verified = await approver.PostAsJsonAsync($"{accounts}/{accountId}/verify", new { verificationReference = reference });
        var verifiedText = await verified.Content.ReadAsStringAsync();
        recorder.CaptureHttp($"{step}-verify-{employee[..8]}", "POST", $"/{accounts}/{accountId}/verify", (int)verified.StatusCode, JsonSerializer.Deserialize<JsonElement>(verifiedText));
        Assert.True(verified.StatusCode == HttpStatusCode.OK, $"{step}: verifying the account of {employee} answered {(int)verified.StatusCode}: {verifiedText}");
        return accountId;
    }
}
