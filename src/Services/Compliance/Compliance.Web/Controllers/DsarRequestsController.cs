using System.Security.Claims;

using System.Text.Json;

using Compliance.Domain.Entities;

using Compliance.Domain.Enums;

using Compliance.Infrastructure.Data;

using Compliance.Infrastructure.Privacy;

using HRManagement.ServiceDefaults;

using HRManagement.ServiceDefaults.Privacy;

using HRManagement.SharedKernel;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;



namespace Compliance.Web.Controllers;



/// <summary>GOV-001-E: DSAR Access/Rectify admin API with PRV-002 collector (AC-27).</summary>

[ApiController]

[Route("api/dsar")]

[Authorize(Roles = $"{HrManagementRoles.Compliance},{HrManagementRoles.PlatformAdmin}")]

public sealed class DsarRequestsController(

    ComplianceDbContext db,

    IDsarDataCollectorService collector) : ControllerBase

{
    private static readonly JsonSerializerOptions PackageJsonOptions = new(JsonSerializerDefaults.Web);


    public sealed record CreateDsarRequest(

        DsarRequestType RequestType,

        Guid? SubjectEmployeeId,

        Guid? SubjectCitizenApplicationId,

        string? SubjectNationalId,

        string? Notes);



    public sealed record UpdateDsarStatusRequest(

        DsarRequestStatus Status,

        string? Notes,

        object? DiscoveryPackage);



    public sealed record DsarDto(

        Guid Id,

        Guid TenantId,

        DsarRequestType RequestType,

        DsarRequestStatus Status,

        Guid? SubjectEmployeeId,

        Guid? SubjectCitizenApplicationId,

        string? SubjectNationalIdHash,

        DateTime RequestedAt,

        DateTime? CompletedAt,

        string? Notes,

        string? DiscoveryPackageJson,

        string? PackageContentHash);



    [HttpGet]

    public async Task<ActionResult<IReadOnlyList<DsarDto>>> List(

        [FromQuery] DsarRequestStatus? status,

        CancellationToken ct)

    {

        var q = db.DsarRequests.AsNoTracking().AsQueryable();

        if (status.HasValue)

            q = q.Where(x => x.Status == status.Value);



        var rows = await q.OrderByDescending(x => x.RequestedAt).Take(100).ToListAsync(ct);

        return Ok(rows.Select(x => ToDto(x)).ToList());

    }



    [HttpGet("{id:guid}")]

    public async Task<ActionResult<DsarDto>> Get(Guid id, CancellationToken ct)

    {

        var entity = await db.DsarRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

        return entity is null ? NotFound() : Ok(ToDto(entity));

    }



    [HttpGet("{id:guid}/package")]

    public async Task<IActionResult> DownloadPackage(Guid id, CancellationToken ct)

    {

        var entity = await db.DsarRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null || string.IsNullOrWhiteSpace(entity.DiscoveryPackageJson))

            return NotFound();



        return Content(entity.DiscoveryPackageJson, "application/json");

    }



    [HttpPost]

    public async Task<ActionResult<DsarDto>> Create([FromBody] CreateDsarRequest req, CancellationToken ct)

    {

        if (req.SubjectEmployeeId is null
            && req.SubjectCitizenApplicationId is null
            && string.IsNullOrWhiteSpace(req.SubjectNationalId))

            return BadRequest("SubjectEmployeeId أو SubjectCitizenApplicationId أو SubjectNationalId مطلوب.");



        var entity = new DsarRequest

        {

            Id = Guid.NewGuid(),

            RequestType = req.RequestType,

            Status = DsarRequestStatus.Open,

            SubjectEmployeeId = req.SubjectEmployeeId,

            SubjectCitizenApplicationId = req.SubjectCitizenApplicationId,

            SubjectNationalIdHash = string.IsNullOrWhiteSpace(req.SubjectNationalId)

                ? null

                : PiiMask.Sha256Hex(req.SubjectNationalId),

            RequestedAt = DateTime.UtcNow,

            Notes = req.Notes,

            CreatedBy = RequireUserId()

        };

        db.DsarRequests.Add(entity);

        await db.SaveChangesAsync(ct);

        SensitiveDataAudit.Log(HttpContext, "Compliance", "DsarCreated", $"DsarId={entity.Id};Type={entity.RequestType}");

        return CreatedAtAction(nameof(Get), new { id = entity.Id }, ToDto(entity));

    }



    [HttpPost("{id:guid}/fulfill")]

    public async Task<ActionResult<DsarDto>> Fulfill(Guid id, CancellationToken ct)

    {

        var entity = await db.DsarRequests.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)

            return NotFound();



        if (entity.Status != DsarRequestStatus.InProgress)

            return BadRequest("يجب أن يكون الطلب InProgress قبل التنفيذ.");



        if (entity.SubjectEmployeeId is null
            && entity.SubjectCitizenApplicationId is null
            && string.IsNullOrWhiteSpace(entity.SubjectNationalIdHash))

            return BadRequest("معرّف الموضوع مطلوب للتجميع.");



        var exporterId = RequireUserId();

        var package = await collector.CollectAsync(entity, exporterId, entity.CreatedBy, ct);

        entity.DiscoveryPackageJson = JsonSerializer.Serialize(package, PackageJsonOptions);

        entity.Status = DsarRequestStatus.Fulfilled;

        entity.CompletedAt = DateTime.UtcNow;



        await db.SaveChangesAsync(ct);

        SensitiveDataAudit.Log(

            HttpContext,

            "Compliance",

            "DsarFulfilled",

            $"DsarId={entity.Id};Exporter={exporterId};Requester={entity.CreatedBy};PackageHash={package.PackageContentHash}");



        return Ok(ToDto(entity, package.PackageContentHash));

    }



    [HttpPost("{id:guid}/status")]

    public async Task<ActionResult<DsarDto>> UpdateStatus(Guid id, [FromBody] UpdateDsarStatusRequest req, CancellationToken ct)

    {

        var entity = await db.DsarRequests.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)

            return NotFound();



        if (!IsAllowedTransition(entity.Status, req.Status))

            return BadRequest($"انتقال غير صالح: {entity.Status} → {req.Status}.");



        entity.Status = req.Status;

        if (!string.IsNullOrWhiteSpace(req.Notes))

            entity.Notes = req.Notes.Trim();



        if (req.DiscoveryPackage is not null)

        {

            entity.DiscoveryPackageJson = JsonSerializer.Serialize(req.DiscoveryPackage, PackageJsonOptions);

        }

        else if (req.Status == DsarRequestStatus.Fulfilled && string.IsNullOrWhiteSpace(entity.DiscoveryPackageJson))

        {

            if (entity.SubjectEmployeeId is null
                && entity.SubjectCitizenApplicationId is null
                && string.IsNullOrWhiteSpace(entity.SubjectNationalIdHash))

                return BadRequest("معرّف الموضوع مطلوب للتجميع.");



            var exporterId = RequireUserId();

            var package = await collector.CollectAsync(entity, exporterId, entity.CreatedBy, ct);

            entity.DiscoveryPackageJson = JsonSerializer.Serialize(package, PackageJsonOptions);

            SensitiveDataAudit.Log(

                HttpContext,

                "Compliance",

                "DsarFulfilled",

                $"DsarId={entity.Id};Exporter={exporterId};Requester={entity.CreatedBy};PackageHash={package.PackageContentHash}");

        }



        if (req.Status is DsarRequestStatus.Fulfilled or DsarRequestStatus.Rejected)

            entity.CompletedAt = DateTime.UtcNow;



        await db.SaveChangesAsync(ct);

        SensitiveDataAudit.Log(HttpContext, "Compliance", "DsarStatus", $"DsarId={entity.Id};Status={entity.Status}");

        return Ok(ToDto(entity));

    }



    [HttpPost("{id:guid}/execute-deletion")]

    public async Task<ActionResult<object>> ExecuteDeletion(Guid id, [FromServices] HRManagement.ServiceDefaults.Privacy.IDsarDeletionOrchestrator orchestrator, CancellationToken ct)

    {

        var entity = await db.DsarRequests.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)

            return NotFound();

        if (entity.RequestType != DsarRequestType.Deletion)

            return BadRequest("تنفيذ الحذف ينطبق فقط على طلبات Deletion.");

        if (entity.SubjectEmployeeId is null
            && entity.SubjectCitizenApplicationId is null)

            return BadRequest("SubjectEmployeeId أو SubjectCitizenApplicationId مطلوب للحذف.");

        if (entity.Status != DsarRequestStatus.InProgress)

            return BadRequest("يمكن تنفيذ الحذف فقط عندما يكون الطلب InProgress.");



        DsarDeletionResult result;
        if (entity.SubjectCitizenApplicationId is Guid citizenAppId
            && entity.SubjectEmployeeId is null)
        {
            result = await orchestrator.ExecuteCandidateAsync(citizenAppId, ct);
        }
        else if (entity.SubjectEmployeeId is Guid employeeId)
        {
            result = await orchestrator.ExecuteAsync(employeeId, ct);
            if (result.Success && entity.SubjectCitizenApplicationId is Guid linkedCitizenAppId)
            {
                var candidateResult = await orchestrator.ExecuteCandidateAsync(linkedCitizenAppId, ct);
                if (!candidateResult.Success)
                    return BadRequest(candidateResult.Error);
            }
        }
        else
        {
            return BadRequest("تكوين موضوع الحذف غير مدعوم.");
        }

        if (!result.Success)

            return BadRequest(result.Error);



        entity.Status = DsarRequestStatus.Fulfilled;

        entity.CompletedAt = DateTime.UtcNow;

        entity.DiscoveryPackageJson = JsonSerializer.Serialize(new

        {

            certificateId = result.CertificateId,

            services = result.ServicesProcessed,

            completedAt = result.CompletedAtUtc

        });

        await db.SaveChangesAsync(ct);

        SensitiveDataAudit.Log(HttpContext, "Compliance", "DsarDeletionExecuted", $"DsarId={entity.Id}");

        return Ok(result);

    }



    private Guid RequireUserId()

    {

        var v = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        return Guid.TryParse(v, out var g)

            ? g

            : throw new UnauthorizedAccessException("Missing user identity claim.");

    }



    private static bool IsAllowedTransition(DsarRequestStatus from, DsarRequestStatus to) => (from, to) switch

    {

        (DsarRequestStatus.Open, DsarRequestStatus.InProgress) => true,

        (DsarRequestStatus.Open, DsarRequestStatus.Rejected) => true,

        (DsarRequestStatus.InProgress, DsarRequestStatus.Fulfilled) => true,

        (DsarRequestStatus.InProgress, DsarRequestStatus.Rejected) => true,

        _ => false

    };



    private static DsarDto ToDto(DsarRequest x, string? packageHash = null)

    {

        string? hash = packageHash;

        if (hash is null && !string.IsNullOrWhiteSpace(x.DiscoveryPackageJson))

        {

            try

            {

                using var doc = JsonDocument.Parse(x.DiscoveryPackageJson);

                if (doc.RootElement.TryGetProperty("packageContentHash", out var h))

                    hash = h.GetString();

            }

            catch (JsonException)

            {

                // ignore malformed legacy packages

            }

        }



        return new DsarDto(

            x.Id,

            x.TenantId,

            x.RequestType,

            x.Status,

            x.SubjectEmployeeId,

            x.SubjectCitizenApplicationId,

            x.SubjectNationalIdHash,

            x.RequestedAt,

            x.CompletedAt,

            x.Notes,

            x.DiscoveryPackageJson,

            hash);

    }

}


