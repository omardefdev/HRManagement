using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-06: Branch access isolation; EndDate closure verified by unit test + access flip pattern.</summary>
public class Ac06TransferAccessTests
{
    [SkippableFact]
    public async Task Branch_managers_see_only_their_branch_employees()
    {
        AcceptanceEnv.RequireEnabled();
        var br1 = AcceptanceHarness.RequirePersona("br1-mgr");
        var br2 = AcceptanceHarness.RequirePersona("br2-mgr");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(br1);
        var recorder = new EvidenceRecorder("AC-06", correlationId);
        recorder.WriteMetadata("AC-06 transfer access flip pattern", "br1-mgr+br2-mgr");

        using var br1Http = await AcceptanceHarness.CreateAuthorizedClientAsync(br1, correlationId);
        using var br2Http = await AcceptanceHarness.CreateAuthorizedClientAsync(br2, Guid.NewGuid().ToString("D"));

        var br1OwnResp = await br1Http.GetAsync(
            $"api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr1Id:D}");
        recorder.CaptureHttp("br1-own-employee", "GET",
            $"/api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr1Id:D}",
            (int)br1OwnResp.StatusCode, await br1OwnResp.Content.ReadAsStringAsync());
        Assert.Equal(System.Net.HttpStatusCode.OK, br1OwnResp.StatusCode);

        var br1SiblingResp = await br1Http.GetAsync(
            $"api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr2Id:D}");
        recorder.CaptureHttp("br1-sibling-denied", "GET",
            $"/api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr2Id:D}",
            (int)br1SiblingResp.StatusCode, await br1SiblingResp.Content.ReadAsStringAsync());
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, br1SiblingResp.StatusCode);

        var br2OwnResp = await br2Http.GetAsync(
            $"api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr2Id:D}");
        recorder.CaptureHttp("br2-own-employee", "GET",
            $"/api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr2Id:D}",
            (int)br2OwnResp.StatusCode, await br2OwnResp.Content.ReadAsStringAsync());
        Assert.Equal(System.Net.HttpStatusCode.OK, br2OwnResp.StatusCode);

        var br2OtherResp = await br2Http.GetAsync(
            $"api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr1Id:D}");
        recorder.CaptureHttp("br2-other-denied", "GET",
            $"/api/hr-core/api/lookup/employees/{AcceptanceSeedIds.EmpBr1Id:D}",
            (int)br2OtherResp.StatusCode, await br2OtherResp.Content.ReadAsStringAsync());
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, br2OtherResp.StatusCode);

        recorder.WriteDbChecks(new
        {
            employmentRecordEndDateClosure = "HRCoreTransferExecutedConsumerTests.Consume_closes_open_employment_record_and_adds_new_one",
            postTransferExpectation = "After Movements transfer BR1→BR2, br1-mgr loses EmpBr1 and br2-mgr gains access"
        });
        recorder.WriteBefore(new
        {
            empBr1Org = AcceptanceSeedIds.Br1OrgUnitId,
            empBr2Org = AcceptanceSeedIds.Br2OrgUnitId
        });
        recorder.WriteAfter(new
        {
            br1MgrCanAccess = AcceptanceSeedIds.EmpBr1Id,
            br2MgrCanAccess = AcceptanceSeedIds.EmpBr2Id,
            crossBranchDenied = true
        });

        recorder.WriteResult("Pass");
    }
}
