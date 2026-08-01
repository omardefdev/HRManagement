using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.ProductCapability;

/// <summary>
/// Returns 403 when the tenant archetype has the service module disabled per PRD-001.
/// Archetype from X-Agency-Archetype header, agency_archetype claim, or CapabilityProfile:Archetype.
/// </summary>
public sealed class CapabilityGuardMiddleware(
    RequestDelegate next,
    ILogger<CapabilityGuardMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICapabilityRegistry registry,
        IOptions<CapabilityProfileOptions> options)
    {
        var opts = options.Value;
        if (!opts.EnforceModuleGuard)
        {
            await next(context);
            return;
        }

        var moduleId = opts.ModuleId;
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            await next(context);
            return;
        }

        var archetype = CapabilityRegistry.ResolveArchetype(context, opts.Archetype);
        if (string.IsNullOrWhiteSpace(archetype))
        {
            await next(context);
            return;
        }

        if (registry.IsModuleEnabled(archetype, moduleId))
        {
            await next(context);
            return;
        }

        logger.LogWarning(
            "PRD-001 capability guard blocked archetype={Archetype} module={ModuleId} path={Path}",
            archetype,
            moduleId,
            context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "module_disabled_for_archetype",
            archetype,
            moduleId,
            card = "PRD-001"
        });
    }
}
