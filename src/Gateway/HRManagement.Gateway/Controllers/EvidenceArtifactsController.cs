using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Evidence;
using HRManagement.SharedKernel.Evidence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Gateway.Controllers;

/// <summary>EVD-003 evidence repository API: ingest (service), read/expired/export (read policy). No delete.</summary>
[ApiController]
[Route("api/platform/evidence/artifacts")]
public sealed class EvidenceArtifactsController(IEvidenceRepositoryService repository) : ControllerBase
{
    public sealed record IngestBody(
        string ArtifactId,
        string CardId,
        string ReleaseDigest,
        string SourceType,
        string ContentBase64,
        string? ControlId = null,
        string? Signature = null,
        DateTime? ExpiresAtUtc = null,
        string Classification = "Internal");

    public sealed record ExportBody(string[] CardIds, string? Destination = null);

    [HttpPost]
    [Authorize(Policy = ServicePolicies.EvidenceIngest)]
    public async Task<ActionResult<EvidenceArtifact>> Ingest([FromBody] IngestBody body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.ArtifactId) || string.IsNullOrWhiteSpace(body.CardId))
            return BadRequest("ArtifactId و CardId مطلوبان.");
        if (string.IsNullOrWhiteSpace(body.ContentBase64))
            return BadRequest("ContentBase64 مطلوب.");

        byte[] content;
        try
        {
            content = Convert.FromBase64String(body.ContentBase64);
        }
        catch (FormatException)
        {
            return BadRequest("ContentBase64 غير صالح.");
        }

        var artifact = await repository.IngestAsync(
            new EvidenceIngestRequest(
                body.ArtifactId,
                body.CardId,
                body.ReleaseDigest ?? "",
                body.SourceType ?? "Manual",
                content,
                body.ControlId,
                body.Signature,
                body.ExpiresAtUtc,
                body.Classification ?? "Internal"),
            ct);

        return CreatedAtAction(nameof(GetById), new { artifactId = artifact.ArtifactId }, artifact);
    }

    [HttpGet("{artifactId}")]
    [Authorize(Policy = HrManagementPolicies.EvidenceRead)]
    public async Task<ActionResult<EvidenceArtifact>> GetById(string artifactId, CancellationToken ct)
    {
        var row = await repository.GetAsync(artifactId, ct);
        return row is null ? NotFound() : Ok(row);
    }

    /// <summary>Lists expired artifacts (visible freshness gaps).</summary>
    [HttpGet("gaps/expired")]
    [Authorize(Policy = HrManagementPolicies.EvidenceRead)]
    public async Task<ActionResult<IReadOnlyList<EvidenceArtifact>>> ListExpired(CancellationToken ct) =>
        Ok(await repository.ListExpiredAsync(ct));

    [HttpPost("export-pack")]
    [Authorize(Policy = HrManagementPolicies.EvidenceRead)]
    public async Task<ActionResult<EvidencePackExport>> ExportPack([FromBody] ExportBody body, CancellationToken ct)
    {
        if (body.CardIds is null || body.CardIds.Length == 0)
            return BadRequest("CardIds مطلوب.");

        var by = User.FindFirstValue("sub") ?? User.Identity?.Name ?? "unknown";
        var pack = await repository.ExportPackAsync(
            body.CardIds,
            by,
            body.Destination ?? "local",
            ct);
        return Ok(pack);
    }
}
