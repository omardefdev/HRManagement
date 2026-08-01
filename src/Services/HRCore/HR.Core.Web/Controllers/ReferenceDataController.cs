using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.Controllers;

/// <summary>MDM-001: CRUD + effective-date query + impact analysis for national reference catalog.</summary>
[Route("api/reference-data")]
[ApiController]
[Authorize(Policy = HrManagementPolicies.HrCoreReadOnly)]
public class ReferenceDataController : ControllerBase
{
    private readonly HRCoreDbContext _db;

    public ReferenceDataController(HRCoreDbContext db) => _db = db;

    public sealed record CatalogEntryDto(
        Guid Id, string CatalogKind, string Code,
        string NameAr, string? NameEn, string? NameFr,
        DateTime? ValidFrom, DateTime? ValidTo,
        string? ReplacedByCode, string? SourceAuthority, string? StewardRole,
        bool IsActive, int Version);

    public sealed record CatalogEntryCreateDto(
        string CatalogKind, string Code,
        string NameAr, string? NameEn, string? NameFr,
        DateTime? ValidFrom, DateTime? ValidTo,
        string? SourceAuthority, string? StewardRole);

    public sealed record CatalogEntryUpdateDto(
        string NameAr, string? NameEn, string? NameFr,
        DateTime? ValidFrom, DateTime? ValidTo,
        string? ReplacedByCode, string? SourceAuthority, string? StewardRole,
        bool IsActive);

    public sealed record ImpactResultDto(string Entity, int AffectedCount);

    // GET api/reference-data?kind=Grade&activeOnly=true
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CatalogEntryDto>>> List(
        [FromQuery] string kind,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<CatalogKind>(kind, true, out var catalogKind))
            return BadRequest($"Unknown CatalogKind: {kind}");

        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, SearchFilterBase.MaxPageSize);

        var query = _db.ReferenceCatalogEntries
            .AsNoTracking()
            .Where(e => e.CatalogKind == catalogKind);

        if (activeOnly)
            query = query.Where(e => e.IsActive);

        var items = await query
            .OrderBy(e => e.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => ToDto(e))
            .ToListAsync(ct);

        return Ok(items);
    }

    // GET api/reference-data/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CatalogEntryDto>> GetById(Guid id, CancellationToken ct)
    {
        var entry = await _db.ReferenceCatalogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        return entry is null ? NotFound() : Ok(ToDto(entry));
    }

    // GET api/reference-data/by-code?kind=Grade&code=GR-01
    [HttpGet("by-code")]
    public async Task<ActionResult<CatalogEntryDto>> GetByCode(
        [FromQuery] string kind, [FromQuery] string code, CancellationToken ct)
    {
        if (!Enum.TryParse<CatalogKind>(kind, true, out var catalogKind))
            return BadRequest($"Unknown CatalogKind: {kind}");

        var entry = await _db.ReferenceCatalogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.CatalogKind == catalogKind && e.Code == code, ct);

        return entry is null ? NotFound() : Ok(ToDto(entry));
    }

    // GET api/reference-data/effective?kind=Grade&code=GR-01&asOf=2026-01-01
    [HttpGet("effective")]
    public async Task<ActionResult<CatalogEntryDto>> ResolveEffective(
        [FromQuery] string kind, [FromQuery] string code, [FromQuery] DateTime asOf,
        CancellationToken ct)
    {
        if (!Enum.TryParse<CatalogKind>(kind, true, out var catalogKind))
            return BadRequest($"Unknown CatalogKind: {kind}");

        var current = await _db.ReferenceCatalogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.CatalogKind == catalogKind && e.Code == code, ct);

        const int maxChain = 10;
        for (var i = 0; i < maxChain && current != null; i++)
        {
            var expired = current.ValidTo.HasValue && current.ValidTo.Value < asOf;
            if (!expired || string.IsNullOrEmpty(current.ReplacedByCode))
                break;

            var replacementCode = current.ReplacedByCode;
            current = await _db.ReferenceCatalogEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.CatalogKind == catalogKind && e.Code == replacementCode, ct);
        }

        return current is null ? NotFound() : Ok(ToDto(current));
    }

    // POST api/reference-data
    [HttpPost]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<ActionResult<CatalogEntryDto>> Create(
        [FromBody] CatalogEntryCreateDto dto, CancellationToken ct)
    {
        if (!Enum.TryParse<CatalogKind>(dto.CatalogKind, true, out var catalogKind))
            return BadRequest($"Unknown CatalogKind: {dto.CatalogKind}");

        var exists = await _db.ReferenceCatalogEntries
            .AnyAsync(e => e.CatalogKind == catalogKind && e.Code == dto.Code, ct);
        if (exists)
            return Conflict($"Code '{dto.Code}' already exists for {dto.CatalogKind}.");

        var entry = new ReferenceCatalogEntry
        {
            CatalogKind = catalogKind,
            Code = dto.Code,
            NameAr = dto.NameAr,
            NameEn = dto.NameEn,
            NameFr = dto.NameFr,
            ValidFrom = dto.ValidFrom,
            ValidTo = dto.ValidTo,
            SourceAuthority = dto.SourceAuthority,
            StewardRole = dto.StewardRole
        };

        _db.ReferenceCatalogEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = entry.Id }, ToDto(entry));
    }

    // PUT api/reference-data/{id}
    [HttpPut("{id:guid}")]
    [Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
    public async Task<ActionResult<CatalogEntryDto>> Update(
        Guid id, [FromBody] CatalogEntryUpdateDto dto, CancellationToken ct)
    {
        var entry = await _db.ReferenceCatalogEntries.FindAsync([id], ct);
        if (entry is null) return NotFound();

        entry.NameAr = dto.NameAr;
        entry.NameEn = dto.NameEn;
        entry.NameFr = dto.NameFr;
        entry.ValidFrom = dto.ValidFrom;
        entry.ValidTo = dto.ValidTo;
        entry.ReplacedByCode = dto.ReplacedByCode;
        entry.SourceAuthority = dto.SourceAuthority;
        entry.StewardRole = dto.StewardRole;
        entry.IsActive = dto.IsActive;
        entry.Version++;

        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(entry));
    }

    // DELETE api/reference-data/{id}
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = HrManagementPolicies.HrCoreDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entry = await _db.ReferenceCatalogEntries.FindAsync([id], ct);
        if (entry is null) return NotFound();

        entry.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET api/reference-data/{id}/impact
    [HttpGet("{id:guid}/impact")]
    public async Task<ActionResult<IReadOnlyList<ImpactResultDto>>> AnalyzeImpact(
        Guid id, CancellationToken ct)
    {
        var entry = await _db.ReferenceCatalogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null) return NotFound();

        Task<int> CountByCode(string entity, string code, CancellationToken c) => entity switch
        {
            "EmploymentRecord" => _db.EmploymentRecords.CountAsync(r => r.JobClassificationCode == code, c),
            "CareerPathStep" => _db.CareerPathSteps.CountAsync(s => s.JobClassificationCode == code, c),
            _ => Task.FromResult(0)
        };

        var results = await ReferenceImpactAnalyzer.AnalyzeAsync(entry, CountByCode, ct);
        return Ok(results.Select(r => new ImpactResultDto(r.Entity, r.AffectedCount)).ToList());
    }

    private static CatalogEntryDto ToDto(ReferenceCatalogEntry e) => new(
        e.Id, e.CatalogKind.ToString(), e.Code,
        e.NameAr, e.NameEn, e.NameFr,
        e.ValidFrom, e.ValidTo,
        e.ReplacedByCode, e.SourceAuthority, e.StewardRole,
        e.IsActive, e.Version);
}
