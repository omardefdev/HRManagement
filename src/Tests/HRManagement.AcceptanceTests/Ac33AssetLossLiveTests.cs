using HRManagement.E2E;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-33: a privileged asset reported lost goes through both approvals; the asset ends Lost, with its settlement and snapshots.</summary>
[RequiredAcceptance]
public class Ac33AssetLossLiveTests
{
    [SkippableFact]
    public async Task A_privileged_loss_is_approved_in_two_steps_and_the_asset_ends_lost_with_its_settlement()
    {
        AcceptanceEnv.RequireEnabled();
        var maker = AcceptanceHarness.RequirePersona("a-logistics-maker");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(maker);
        var recorder = new EvidenceRecorder("AC-33", correlationId);
        recorder.WriteMetadata("AC-33 a privileged loss through both approvals, with its settlement and snapshots", maker.Id);
        recorder.WriteSteps(
            "1. a-logistics-maker reports a privileged asset lost through its page: PendingApproval with an instance.\n" +
            "2. a-logistics-checker approves step 1 and a-management-approver step 2, from the inbox, on the generic page.\n" +
            "3. The report is Confirmed, the asset Lost, the settlement recorded and both snapshots written (psql).");

        var (asset, report, instance) = await CustodyJourney.LossThroughBothStepsAsync(recorder);

        var settled = await CustodyJourney.CountAsync("admin_db",
            $"SELECT count(*) FROM \"AssetLossReports\" WHERE \"Id\"='{report:D}' AND \"SettlementApprovedAt\" IS NOT NULL AND \"SettlementApprovedBy\" IS NOT NULL AND \"ConfirmedLostAt\" IS NOT NULL AND \"BeforeStateJson\" IS NOT NULL AND \"AfterStateJson\" IS NOT NULL;");
        Assert.Equal(1, settled);

        recorder.WriteDbChecks(new { asset, report, instance, assetStatus = "Lost", settled });
        recorder.WriteAfter(new { status = "Confirmed", instance });
        recorder.WriteResult("Pass");
    }
}
