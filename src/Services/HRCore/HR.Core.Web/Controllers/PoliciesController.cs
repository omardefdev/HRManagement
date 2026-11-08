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
[ApiController]
[Authorize(Policy = HrManagementPolicies.PolicyReader)]
public class PoliciesController(
    HRCoreDbContext db,
    IPolicyOrchestrator orchestrator,
    PolicyPublishService publishService,
    PolicyDraftService drafts) : ControllerBase
{
    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate([FromBody] PolicyEnforceRequest request, CancellationToken ct)
    {
        // PLAN-42: a user reads the decision of their own tenant; the body names one only for a platform operator with no session.
        request.TenantId = HttpContext.Items[TenantIdMiddleware.ItemKey] as Guid? ?? request.TenantId;
        // PLAN-47: "as of a date" is for people reading HR.Core's policy; a service is decided on the policy in force now.
        if (ServiceCallerPrincipal.IsServiceAccount(User))
            request.AsOfUtc = null;
        var decision = await orchestrator.EvaluateAsync(request, ct);
        return Ok(decision);
    }

    /// <summary>PLAN-47: one version with its rules, its editors and its revision (what an approver reviews before publishing).</summary>
    [HttpGet("versions/{versionId:guid}")]
    public async Task<IActionResult> GetVersion(Guid versionId, CancellationToken ct)
    {
        var version = await db.PolicyTemplateVersions
            .Include(v => v.Rules)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null) return NotFound();
        var editors = await db.PolicyVersionEdits.AsNoTracking()
            .Where(e => e.PolicyTemplateVersionId == versionId)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new { e.UserId, Action = e.Action.ToString(), e.RuleKey, e.OccurredAt })
            .ToListAsync(ct);
        return Ok(new
        {
            version.Id,
            version.PolicyTemplateId,
            version.VersionNumber,
            Status = version.Status.ToString(),
            version.EffectiveFrom,
            version.EffectiveTo,
            version.CreatedBy,
            version.RollbackOfVersionId,
            version.ChangeReason,
            version.WithdrawsAllRules,
            Revision = publishService.Revision(version),
            Rules = version.Rules.OrderBy(r => r.Priority).ThenBy(r => r.RuleKey)
                .Select(r => new { r.Id, r.RuleKey, r.Name, r.Priority, r.WhenJson, r.ThenJson }),
            Editors = editors
        });
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

    /// <summary>
    /// PLAN-47 (D-POL-3): publish a draft. The approver policy, and a publisher who is not among the draft's editors; no
    /// override in the body. The one publish path: PLAN-40's IPolicyPublished is staged inside the publisher, before its save.
    /// </summary>
    [HttpPost("templates/{versionId:guid}/publish")]
    [Authorize(Policy = HrManagementPolicies.PolicyApprover)]
    public async Task<IActionResult> Publish(Guid versionId, [FromBody] PublishPolicyRequest? request, CancellationToken ct)
    {
        var result = await publishService.PublishAsync(
            new PolicyPublishCommand(versionId, new PolicyActor(ParseUserId(User), User), request?.EvidenceRef, request?.Revision), ct);
        if (!result.Succeeded)
            return PublishProblem(result);

        var version = result.Version!;
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

    /// <summary>PLAN-47 (§5): a new draft of the template, a copy of the effective version unless another is named.</summary>
    [HttpPost("templates/{templateId:guid}/drafts")]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> CreateDraft(Guid templateId, [FromBody] CreatePolicyDraftRequest? request, CancellationToken ct)
    {
        var userId = ParseUserId(User);
        var result = request?.WithdrawAllRules == true
            ? await drafts.CreateWithdrawAllDraftAsync(templateId, request.Reason, userId, ct)
            : await drafts.CreateDraftAsync(templateId, request?.CopyFromVersionId, userId, ct);
        return DraftResult(result);
    }

    /// <summary>PLAN-47 (§7): a rollback draft: a copy of an earlier version, with a written reason.</summary>
    [HttpPost("templates/{templateId:guid}/rollback-drafts")]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> CreateRollbackDraft(Guid templateId, [FromBody] CreateRollbackDraftRequest request, CancellationToken ct)
    {
        var result = await drafts.CreateRollbackDraftAsync(templateId, request.TargetVersionId, request.Reason, ParseUserId(User), ct);
        return DraftResult(result);
    }

    /// <summary>PLAN-47: add a rule to a draft. The rule is validated before it is saved; a published version answers 409.</summary>
    [HttpPost("versions/{versionId:guid}/rules")]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> AddRule(Guid versionId, [FromBody] PolicyRuleRequest request, CancellationToken ct)
    {
        var result = await drafts.AddRuleAsync(versionId, request.ToInput(), ParseUserId(User), ct);
        return DraftResult(result);
    }

    /// <summary>PLAN-47: change a rule of a draft.</summary>
    [HttpPut("versions/{versionId:guid}/rules/{ruleId:guid}")]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> UpdateRule(Guid versionId, Guid ruleId, [FromBody] PolicyRuleRequest request, CancellationToken ct)
    {
        var result = await drafts.UpdateRuleAsync(versionId, ruleId, request.ToInput(), ParseUserId(User), ct);
        return DraftResult(result);
    }

    /// <summary>PLAN-47: remove a rule from a draft.</summary>
    [HttpDelete("versions/{versionId:guid}/rules/{ruleId:guid}")]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> RemoveRule(Guid versionId, Guid ruleId, CancellationToken ct)
    {
        var result = await drafts.RemoveRuleAsync(versionId, ruleId, ParseUserId(User), ct);
        return DraftResult(result);
    }

    /// <summary>PLAN-47 (§6): when the draft takes effect once published (UTC); null means at publication.</summary>
    [HttpPut("versions/{versionId:guid}/effective-from")]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> SetEffectiveFrom(Guid versionId, [FromBody] SetEffectiveFromRequest request, CancellationToken ct)
    {
        var result = await drafts.SetEffectiveFromAsync(versionId, request.EffectiveFromUtc, ParseUserId(User), ct);
        return DraftResult(result);
    }

    /// <summary>PLAN-47 (§5): discard a draft (it becomes Archived).</summary>
    [HttpPost("versions/{versionId:guid}/discard")]
    [Authorize(Policy = HrManagementPolicies.PolicyAuthor)]
    public async Task<IActionResult> DiscardDraft(Guid versionId, CancellationToken ct)
    {
        var result = await drafts.DiscardDraftAsync(versionId, ParseUserId(User), ct);
        return DraftResult(result);
    }

    private IActionResult DraftResult(PolicyDraftResult result) => result.Outcome switch
    {
        PolicyDraftOutcome.Ok => Ok(new { id = result.Id }),
        PolicyDraftOutcome.NotFound => NotFound(new { error = result.MessageAr }),
        PolicyDraftOutcome.NotDraft or PolicyDraftOutcome.Conflict => Conflict(new { error = result.MessageAr }),
        _ => BadRequest(new { error = result.MessageAr, issues = result.IssueList })
    };

    private IActionResult PublishProblem(PolicyPublishResult result) => result.Outcome switch
    {
        PolicyPublishOutcome.NotFound => NotFound(new { error = result.MessageAr }),
        PolicyPublishOutcome.Conflict => Conflict(new { error = result.MessageAr }),
        PolicyPublishOutcome.FourEyes => StatusCode(StatusCodes.Status403Forbidden, new { error = result.MessageAr }),
        _ => BadRequest(new { error = result.MessageAr, issues = result.IssueList })
    };

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

public sealed class PolicyRuleRequest
{
    public string? RuleKey { get; set; }
    public string? Name { get; set; }
    public int Priority { get; set; } = 100;
    public string? WhenJson { get; set; }
    public string? ThenJson { get; set; }

    public HR.Core.Domain.Services.Policy.PolicyRuleInput ToInput() => new(RuleKey, Name, Priority, WhenJson, ThenJson);
}

/// <summary>PLAN-47: there is no same-actor override; an unknown property in the body (an old client's flag) is ignored.</summary>
public sealed class PublishPolicyRequest
{
    public string? EvidenceRef { get; set; }

    /// <summary>The revision of the draft the approver reviewed (from GET versions/{id}); a change since then answers 409.</summary>
    public uint? Revision { get; set; }
}

public sealed class CreatePolicyDraftRequest
{
    public Guid? CopyFromVersionId { get; set; }

    /// <summary>The explicit "withdraw every rule of the domain" draft; needs <see cref="Reason"/>.</summary>
    public bool WithdrawAllRules { get; set; }

    public string? Reason { get; set; }
}

public sealed class CreateRollbackDraftRequest
{
    public Guid TargetVersionId { get; set; }
    public string? Reason { get; set; }
}

public sealed class SetEffectiveFromRequest
{
    public DateTime? EffectiveFromUtc { get; set; }
}
