using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>
/// PLAN-43 (G-WF-04): at start-up, the validator over every published definition. It warns and counts
/// (<c>workflow_definition_invalid_total</c>) and never stops the boot: the known cases (<c>security</c>, <c>it-support</c>, types
/// no service owns yet) are PLAN-46's to correct, by retiring them or by a new version.
/// </summary>
public static class WorkflowDefinitionHealthReport
{
    public static async Task<IReadOnlyList<WorkflowDefinition>> RunAsync(HRCoreDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var published = await db.WorkflowDefinitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .AsNoTracking()
            .Where(d => d.IsPublished)
            .OrderBy(d => d.Code)
            .ThenBy(d => d.Version)
            .ToListAsync(ct);

        var invalid = new List<WorkflowDefinition>();
        foreach (var definition in published)
        {
            var errors = WorkflowDefinitionCatalog.Validate(definition);
            if (errors.Count == 0)
                continue;

            invalid.Add(definition);
            logger.LogWarning(
                "Published workflow definition {Code} v{Version} fails validation: {Errors}",
                definition.Code, definition.Version, string.Join(" | ", errors));
        }

        if (invalid.Count > 0)
            WorkflowMetrics.DefinitionInvalid.Add(invalid.Count);

        logger.LogInformation(
            "Workflow definition health: {Invalid} of {Published} published definitions are invalid",
            invalid.Count, published.Count);
        return invalid;
    }
}
