using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>
/// PLAN-49 (G-POL-09), acceptance only: one more rule on the national Core.Recruitment policy, at the first priority: every
/// offer that is issued fires the ordinary offer approval. The catalog accepts it (an offer may trigger either offer workflow;
/// no validator can know which offer a rule will meet), and in Enforce it would swap the stricter approval of an exception
/// offer for the ordinary one. AC-POL-49f proves that the approval start refuses that.
/// <para>
/// docker-compose.acceptance-pol49.yml turns it on with <c>Seed:Pol49WeakOfferRule=true</c>, beside
/// <c>PolicyEngine:Core.Recruitment=Enforce</c>, for the pol49 stage of tests/acceptance/ci-scope.json only. It is applied as a
/// change set through <see cref="PolicySeedChangeSetApplier"/> (a new published version, recorded once), and it is never a member
/// of <see cref="PolicySeedChangeSets.All"/>: no other stack, and no server, ever holds it.
/// </para>
/// </summary>
public static class AcceptancePol49WeakOfferPatch
{
    public const string SwitchKey = "Seed:Pol49WeakOfferRule";
    public const string ChangeSetId = "ACC-POL49-WEAK-OFFER";

    /// <summary>The rule carries the set's own name: one literal, written once.</summary>
    public const string RuleKey = ChangeSetId;

    public static readonly PolicySeedChangeSet ChangeSet = new(ChangeSetId,
    [
        new(PolicySeedOperationKind.AddRule, PolicyDomains.CoreRecruitment, RuleKey, "قبول PLAN-49: كل عرض ← اعتماد العرض العادي", 1,
            """{"all":[{"field":"action","operator":"Equals","value":"Issue"}]}""",
            """[{"effect":"TriggerWorkflow","params":{"workflowCode":"RECRUITMENT-OFFER-APPROVAL"}}]""")
    ]);

    public static async Task ApplyAsync(HRCoreDbContext db, IServiceProvider services, CancellationToken ct = default)
    {
        if (services.GetService<IConfiguration>()?.GetValue(SwitchKey, false) != true)
            return;

        // The national template, read and written in the national tenant, as the start-up change sets are.
        var previousTenant = TenantIdFlow.Get();
        TenantIdFlow.Set(TenantDefaults.DefaultTenantId);
        try
        {
            await PolicySeedChangeSetApplier.Create(db, services).ApplyAsync([ChangeSet], ct);
        }
        finally
        {
            TenantIdFlow.Set(previousTenant);
        }
    }
}
