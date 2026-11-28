using System.Net;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.OrgScope;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-60 (AC-SEP-60d, the half without the gateway): HR.Contracts itself, on PostgreSQL, behind no gateway. The management approver
/// reads the separation pages (PLAN-51: reads take the module policy) but is refused the execution by the action's own policy
/// (<c>HrContracts.Approve</c>), on the wizard's <c>Step3Execute</c> and on the Details page's <c>Execute</c> alike; a holder of
/// <c>hr-contracts</c> passes the policy. The gateway's half is AC-SEP-60d's acceptance test.
/// </summary>
[Collection("PostgreSql")]
public sealed class Sep060ServicePolicyHttpTests(PostgreSqlFixture postgres)
{
    private static Task<Dictionary<Type, Guid>> SeedApprovedAsync(ContractsDbContext db, Guid unit)
    {
        var record = new SeparationRecord
        {
            TenantId = HRManagement.SharedKernel.Tenancy.TenantDefaults.DefaultTenantId,
            EmployeeId = Guid.NewGuid(),
            SeparationType = SeparationType.Resignation,
            EffectiveDate = new DateOnly(2026, 11, 1)
        };
        SeparationWorkflowRules.Transition(record, SeparationStatus.PendingApproval, Guid.NewGuid(), null,
            new SeparationSubmission(Guid.NewGuid(), "SEPARATION-APPROVAL", 1));
        SeparationWorkflowRules.Transition(record, SeparationStatus.Approved, Guid.NewGuid());
        db.SeparationRecords.Add(record);
        return db.SaveChangesAsync().ContinueWith(_ => new Dictionary<Type, Guid> { [typeof(SeparationRecord)] = record.Id });
    }

    private static async Task<string> TokenAsync(HttpClient client, string page)
    {
        using var response = await client.GetAsync(page);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = System.Text.RegularExpressions.Regex.Match(await response.Content.ReadAsStringAsync(),
            "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, $"{page} renders no antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Without_the_gateway_the_management_approver_is_refused_the_execution_by_the_actions_policy()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await Iam052PgServiceHost<HR.Contracts.Web.Controllers.HireWizardController, ContractsDbContext>.StartAsync(
            postgres, "ContractsDb", ContractsOrgScopeKeys.Registry,
            HrManagementRoles.HrManagementApprover,
            HrManagementRoles.HrManagementApprover,
            "/SeparationRecords/Create",
            SeedApprovedAsync);
        var record = host.Records[typeof(SeparationRecord)].Own;

        using (var approver = host.Central())
        {
            // He reads (the module policy), and his writes stop at the action's policy: 403 from the service, not from a gateway.
            var token = await TokenAsync(approver, "/SeparationRecords/Create");
            foreach (var target in new[] { $"/SeparationWizard/Step3Execute/{record}", $"/SeparationRecords/Execute/{record}" })
            {
                using var response = await approver.PostAsync(target, new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        // A holder of hr-contracts passes the policy (the handler then answers: a redirect, never a 403).
        using (var checker = host.WithRoles(HrManagementRoles.HrContracts, HrManagementRoles.HrContracts).Central())
        {
            var token = await TokenAsync(checker, "/SeparationRecords/Create");
            using var response = await checker.PostAsync($"/SeparationRecords/Execute/{record}",
                new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.True((int)response.StatusCode < 500, $"{(int)response.StatusCode}");
        }

        Assert.Equal(SeparationStatus.Approved, await host.WithDbAsync(async db =>
            (await ((ContractsDbContext)db).SeparationRecords.AsNoTracking().SingleAsync(r => r.Id == record)).Status));
    }
}
