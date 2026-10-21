using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Services.Policy;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Policy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRManagement.MessageContracts.Events;
using MassTransit;

namespace HR.Core.Web.Controllers;

[Route("api/policies")]
[Route("api/internal/policies")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class PoliciesController(
    HRCoreDbContext db,
    IPolicyOrchestrator orchestrator,
    IPolicyVersionPublisher publisher,
    IPublishEndpoint? publish = null) : ControllerBase
{
    [HttpPost("enforce")]
    [Authorize(Policy = HrManagementPolicies.HrCoreBranchOperation)]
    public async Task<IActionResult> Enforce([FromBody] PolicyEnforceRequest request, CancellationToken ct)
    {
        request.ActorUserId ??= ParseUserId(User);
        request.ActorUserName ??= User.FindFirstValue("preferred_username") ?? User.Identity?.Name;
        var response = await orchestrator.EnforceAsync(request, ct);
        if (!response.Allowed && request.Mode == PolicyEnforcementMode.Enforce)
            return UnprocessableEntity(response);
        return Ok(response);
    }

    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate([FromBody] PolicyEnforceRequest request, CancellationToken ct)
    {
        var decision = await orchestrator.EvaluateAsync(request, ct);
        return Ok(decision);
    }

    [HttpGet("snapshots/{domain}")]
    public async Task<IActionResult> GetSnapshot(string domain, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var snapshot = await orchestrator.GetPublishedSnapshotAsync(domain, tenantId, null, ct);
        if (snapshot is null) return NotFound();
        return Ok(snapshot);
    }

    [HttpGet("templates")]
    public async Task<IActionResult> ListTemplates([FromQuery] string? domain, CancellationToken ct)
    {
        var q = db.PolicyTemplates.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(domain))
            q = q.Where(t => t.Domain == domain);
        var items = await q.OrderBy(t => t.Domain).ThenBy(t => t.Name).ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("templates/{id:guid}")]
    public async Task<IActionResult> GetTemplate(Guid id, CancellationToken ct)
    {
        var t = await db.PolicyTemplates
            .Include(x => x.Versions)
            .ThenInclude(v => v.Rules)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null ? NotFound() : Ok(t);
    }

    [HttpPost("templates/{versionId:guid}/simulate")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<IActionResult> Simulate(Guid versionId, [FromBody] Dictionary<string, string> facts, CancellationToken ct)
    {
        var version = await db.PolicyTemplateVersions
            .Include(v => v.PolicyTemplate)
            .Include(v => v.Rules)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null) return NotFound();

        var schemaEntity = await db.PolicyFactsSchemas.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Domain == version.PolicyTemplate.Domain, ct);
        var snapshot = PolicyEntityMapper.ToSnapshot(version.PolicyTemplate, version, PolicyEntityMapper.ParseSchema(schemaEntity));
        var decision = PolicyEvaluator.Evaluate(snapshot, facts);

        db.PolicySimulationRuns.Add(new PolicySimulationRun
        {
            PolicyTemplateVersionId = versionId,
            FactsJson = JsonSerializer.Serialize(facts),
            DecisionJson = JsonSerializer.Serialize(decision),
            RunByUserId = ParseUserId(User),
            RunAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return Ok(decision);
    }

    [HttpPost("templates/{versionId:guid}/publish")]
    [Authorize(Policy = HrManagementPolicies.HrCoreApprove)]
    public async Task<IActionResult> Publish(Guid versionId, [FromBody] PublishPolicyRequest request, CancellationToken ct)
    {
        var version = await db.PolicyTemplateVersions
            .Include(v => v.PolicyTemplate)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null) return NotFound();
        if (version.Status != PolicyVersionStatus.Draft)
            return BadRequest("يمكن نشر المسودات فقط");

        var makerId = version.CreatedBy ?? ParseUserId(User);
        var checkerId = ParseUserId(User);
        if (makerId == checkerId && !request.ForceSameActorForLab)
            return BadRequest("four-eyes: الناشر يجب أن يختلف عن المحرّر");

        await publisher.PublishDraftAsync(version, makerId, checkerId, request.EvidenceRef, ct);

        if (publish is not null)
        {
            await publish.Publish(new PolicyPublished
            {
                Domain = version.PolicyTemplate.Domain,
                VersionId = versionId,
                PublishedAt = DateTime.UtcNow
            }, ct);
        }

        return Ok(new
        {
            version.Id,
            version.PolicyTemplateId,
            version.VersionNumber,
            version.Status,
            version.EffectiveFrom,
            version.EffectiveTo,
            version.TenantId
        });
    }

    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger([FromQuery] string? domain, [FromQuery] string? correlationId, CancellationToken ct)
    {
        var q = db.PolicyEnforcementLedgers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(domain))
            q = q.Where(l => l.Domain == domain);
        if (!string.IsNullOrWhiteSpace(correlationId))
            q = q.Where(l => l.CorrelationId == correlationId);
        var items = await q.OrderByDescending(l => l.OccurredAt).Take(200).ToListAsync(ct);
        return Ok(items);
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}

public sealed class PublishPolicyRequest
{
    public string? EvidenceRef { get; set; }
    public bool ForceSameActorForLab { get; set; }
}
