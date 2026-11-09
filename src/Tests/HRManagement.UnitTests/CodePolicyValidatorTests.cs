using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;

namespace HRManagement.UnitTests;

/// <summary>PLAN-48 (§3): the five rules of the code policy validator, and every seeded policy passes them.</summary>
public class CodePolicyValidatorTests
{
    private static IReadOnlyList<string> Codes(CodeEntityKind kind, string segments, CodeSequenceReset reset) =>
        CodePolicyValidator.ValidateVersion(kind, segments, reset).Select(i => i.Code).ToList();

    [Fact]
    public void Every_policy_the_seeder_writes_passes_and_there_is_one_national_default_per_kind()
    {
        foreach (var policy in CodeGenerationSeeder.NationalPolicies)
            Assert.True(CodePolicyValidator.ValidateVersion(policy.Kind, policy.Format).Count == 0, $"{policy.Kind} {policy.PolicyKey}");

        Assert.Empty(CodePolicyValidator.ValidateDefaults(CodeGenerationSeeder.NationalPolicies.Select(p =>
            new CodePolicyTemplateKey(NationalTenant.Id, p.Kind, CodePolicyScope.National, p.PolicyKey))));
        Assert.Equal(CodeGenerationSeeder.NationalPolicies.Count,
            CodeGenerationSeeder.NationalPolicies.Select(p => (p.Kind, p.PolicyKey)).Distinct().Count());
    }

    [Fact]
    public void Rule_1_one_sequence_and_none_in_a_derived_kind()
    {
        Assert.Contains("SEQUENCE_COUNT", Codes(CodeEntityKind.LegalCase, """[{"kind":"Literal","value":"LC-"},{"kind":"Date","value":"YYYY"}]""", CodeSequenceReset.Yearly));
        Assert.Contains("SEQUENCE_COUNT", Codes(CodeEntityKind.LegalCase,
            """[{"kind":"Literal","value":"LC-"},{"kind":"Date","value":"YYYY"},{"kind":"Sequence","padWidth":2},{"kind":"Sequence","padWidth":2}]""", CodeSequenceReset.Yearly));
        Assert.Contains("DERIVED_HAS_SEQUENCE", Codes(CodeEntityKind.CostCenter,
            """[{"kind":"Field","value":"OrgUnitCode"},{"kind":"Sequence","padWidth":2,"scopeFields":["OrgUnitCode"]}]""", CodeSequenceReset.PerScope));
        Assert.Empty(Codes(CodeEntityKind.CostCenter, """[{"kind":"Field","value":"OrgUnitCode"}]""", CodeSequenceReset.Never));
    }

    [Fact]
    public void Rule_2_every_scope_field_shows_in_the_number()
    {
        // G-COD-03 (b) and (c): the two defective national v1 policies.
        Assert.Contains("SCOPE_FIELD_NOT_SHOWN", Codes(CodeEntityKind.FundedPosition, CodePolicyUpgrades.FundedPositionV1Json, CodeSequenceReset.PerScope));
        Assert.Contains("SCOPE_FIELD_NOT_SHOWN", Codes(CodeEntityKind.RecruitmentRequisition, CodePolicyUpgrades.RecruitmentRequisitionV1Json, CodeSequenceReset.Yearly));
        Assert.Empty(Codes(CodeEntityKind.FundedPosition, CodePolicyUpgrades.FundedPositionV2Json, CodeSequenceReset.PerScope));
        Assert.Empty(Codes(CodeEntityKind.RecruitmentRequisition, CodePolicyUpgrades.RecruitmentRequisitionV2Json, CodeSequenceReset.PerScope));
    }

    [Fact]
    public void Rule_3_a_yearly_reset_shows_the_year()
    {
        const string Fin = """[{"kind":"Literal","value":"FIN-"},{"kind":"Field","value":"ParentCode"},{"kind":"Literal","value":"."},{"kind":"Sequence","padWidth":2,"scopeFields":["ParentCode"]}]""";
        // The FIN-ORG-UNIT overlay before PLAN-48 had no reset, so the entity default Yearly: it would restart each January.
        Assert.Contains("YEARLY_WITHOUT_YEAR", Codes(CodeEntityKind.OrgUnit, Fin, CodeSequenceReset.Yearly));
        Assert.Empty(Codes(CodeEntityKind.OrgUnit, Fin, CodeSequenceReset.PerScope));
        Assert.Empty(Codes(CodeEntityKind.LegalCase, """[{"kind":"Literal","value":"LC-"},{"kind":"Date","value":"YY"},{"kind":"Sequence","padWidth":4}]""", CodeSequenceReset.Yearly));
    }

    [Fact]
    public void Rule_4_a_per_scope_reset_has_scope_fields()
    {
        Assert.Contains("PERSCOPE_WITHOUT_FIELDS", Codes(CodeEntityKind.OrgUnit, """[{"kind":"Literal","value":"OU-"},{"kind":"Sequence","padWidth":3}]""", CodeSequenceReset.PerScope));
    }

    [Fact]
    public void Rule_5_one_default_overlay_per_kind_in_an_agency_and_one_national_default()
    {
        var agency = Guid.NewGuid();
        var overlays = CodePolicyValidator.ValidateDefaults(
        [
            new(agency, CodeEntityKind.OrgUnit, CodePolicyScope.TenantOverride, null),
            new(agency, CodeEntityKind.OrgUnit, CodePolicyScope.TenantOverride, null),
            new(agency, CodeEntityKind.OrgUnit, CodePolicyScope.TenantOverride, "KEYED"),
            new(Guid.NewGuid(), CodeEntityKind.OrgUnit, CodePolicyScope.TenantOverride, null)
        ]);
        Assert.Equal(new[] { "OVERLAY_DEFAULT_DUPLICATE" }, overlays.Select(i => i.Code));

        var national = CodePolicyValidator.ValidateDefaults(
        [
            new(NationalTenant.Id, CodeEntityKind.AssetTag, CodePolicyScope.National, null),
            new(NationalTenant.Id, CodeEntityKind.AssetTag, CodePolicyScope.National, null),
            new(NationalTenant.Id, CodeEntityKind.AssetTag, CodePolicyScope.National, "ASSET-PRIVILEGED")
        ]);
        Assert.Equal(new[] { "NATIONAL_DEFAULT_DUPLICATE" }, national.Select(i => i.Code));
    }

    [Fact]
    public void Malformed_segments_are_an_issue_not_an_exception()
    {
        Assert.Equal(new[] { "SEGMENTS_JSON" }, Codes(CodeEntityKind.LegalCase, "{not json", CodeSequenceReset.Yearly));
    }
}
