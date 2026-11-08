using System.Text.Json;
using System.Text.RegularExpressions;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HRManagement.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-46 (section 2, section 6): every registered type is enforced everywhere it is configured, every code it allows that the seed knows
/// passes the validator, and the registry and the consumer guard name the same types.
/// </summary>
public sealed class Wf046RegistryEnforcedTests
{
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Staging.json")]
    [InlineData("appsettings.Production.json")]
    public void No_registered_type_is_exempt_in_a_settings_file(string file)
    {
        using var settings = JsonDocument.Parse(global::System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Services", "HRCore", "HR.Core.Web", file)));
        var exempt = settings.RootElement.GetProperty("Workflow").GetProperty("StepRole").GetProperty("ExemptEntityTypes")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Empty(exempt);
    }

    [Fact]
    public void No_type_is_exempt_on_the_prod_like_stack() =>
        Assert.DoesNotMatch(new Regex("ExemptEntityTypes__"), global::System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "docker-compose.acceptance-prodlike.yml")));

    [Fact]
    public void The_exemption_registry_has_no_open_row()
    {
        using var registry = JsonDocument.Parse(global::System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "docs", "qa", "workflow-step-role-exemptions.json")));

        Assert.All(registry.RootElement.GetProperty("exemptions").EnumerateArray(),
            row => Assert.Equal("Closed", row.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Every_seeded_code_of_a_registered_type_passes_the_validator()
    {
        await using var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
        await WorkflowPackSeeder.EnsureCrossServiceChainsAsync(db);

        var published = await db.WorkflowDefinitions.AsNoTracking().Where(d => d.IsPublished).ToListAsync();
        foreach (var type in ExternalApprovalTypes.All)
        {
            foreach (var code in type.AllowedWorkflowCodes)
            {
                var definition = published.Where(d => d.Code == code).MaxBy(d => d.Version);
                if (definition is null)
                {
                    // Only the separation codes have no seed here: PLAN-60 seeds them, and until then a start answers DefinitionMissing.
                    Assert.Equal(ExternalApprovalTypes.SeparationRecordEntityType, type.EntityType);
                    continue;
                }

                Assert.Equal(type.EntityType, definition.DomainEntityType);
                Assert.Empty(WorkflowDefinitionCatalog.Validate(definition));
            }
        }
    }
}

public sealed class Wf046RegistryConsistencyTests
{
    [Fact]
    public void Every_generic_type_has_a_consumer_entry_and_every_entry_is_registered()
    {
        var generic = ExternalApprovalTypes.All
            .Where(t => t.OutcomeEvent == ExternalOutcomeEvent.Generic)
            .Select(t => t.EntityType)
            .Order(StringComparer.Ordinal);

        Assert.Equal(generic, WorkflowDefinitionsHaveHandlersTests.OutcomeConsumers.Keys.Order(StringComparer.Ordinal));
    }
}
