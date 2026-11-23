using System.Globalization;
using System.Text;
using System.Text.Json;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.ViewModels;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollConfigurations;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

/// <summary>
/// PLAN-54 (§1): the payroll legal frameworks over the API. A holder of <c>Payroll.MasterData</c> creates a draft, imports a legal pack,
/// exports a version and sets a draft's effective date. Every write names its policy and validates an antiforgery token (the API is
/// reached with a gateway session as well as with a bearer token). The answers: 400 a broken rule (with each error of a pack: the
/// field, the row, an Arabic message, never the file's content), 409 a version that is not a draft, 404 no such version.
/// </summary>
[ApiController]
[Route("api/payroll/frameworks")]
[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
[OrgScopeNotApplicable("a payroll legal framework belongs to the agency, not to a branch")]
public sealed class PayrollFrameworksApiController(PayrollDbContext db, IMediator mediator) : ControllerBase
{
    /// <summary>
    /// The antiforgery token every POST and PUT of this controller must send (the header's name and its value); the matching cookie is
    /// set on this response (as PLAN-53's salary change API).
    /// </summary>
    [HttpGet("antiforgery")]
    public MessagingRepairAntiforgery Antiforgery([FromServices] IAntiforgery antiforgery) =>
        MessagingRepairAntiforgery.Issue(antiforgery, HttpContext);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? code, CancellationToken ct)
    {
        var query = db.PayrollConfigurations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(code))
        {
            var wanted = code.Trim();
            query = query.Where(c => c.Code == wanted);
        }

        var items = await query.OrderBy(c => c.Code).ThenByDescending(c => c.VersionNumber).Take(SearchFilterBase.MaxPageSize).ToListAsync(ct);
        return Ok(items.Select(PayrollFrameworkDto.From));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var version = await db.PayrollConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        return version is null ? NotFound() : Ok(PayrollFrameworkDto.From(version));
    }

    /// <summary>A draft from a regime's template (<c>regimeCode</c>) or as a clone of a version (<c>cloneFromId</c>).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Create([FromBody] CreatePayrollFrameworkApiRequest request, CancellationToken ct)
    {
        if (!PayrollFrameworkHttp.TryDate(request.EffectiveFrom, out var effectiveFrom))
            return PayrollFrameworkHttp.Refusal(PayrollFrameworkResult.Invalid(PayrollFrameworkHttp.DateFormat));

        var result = await mediator.Send(new CreatePayrollFrameworkCommand(
            request.RegimeCode, request.CloneFromId, request.Code, request.NameAr, effectiveFrom, PayProfileChangeHttp.RequireUserId(User)), ct);
        return result.Success
            ? CreatedAtAction(nameof(Get), new { id = result.FrameworkId }, PayrollFrameworkHttp.Body(result))
            : PayrollFrameworkHttp.Refusal(result);
    }

    /// <summary>
    /// Imports a legal pack. The body is a JSON object: <c>pack</c> (the pack itself), and optionally <c>code</c> and
    /// <c>effectiveFrom</c> (in place of the pack's own) and <c>targetDraftId</c> (replace that draft's content). The body is read here,
    /// not by the model binder: a body that cannot be read is answered in Arabic without repeating any of it.
    /// </summary>
    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    [Consumes("application/json")]
    [RequestSizeLimit(PayrollFrameworkUpload.MaxRequestBytes)]
    public async Task<IActionResult> Import(CancellationToken ct)
    {
        var (body, readError) = await PayrollFrameworkUpload.ReadAsync(Request.Body, (int)PayrollFrameworkUpload.MaxRequestBytes, ct);
        if (readError is not null)
            return PayrollFrameworkHttp.Refusal(PayrollFrameworkResult.Invalid(readError));
        if (!PayrollFrameworkHttp.TryReadImportEnvelope(body, out var envelope, out var envelopeError))
            return PayrollFrameworkHttp.Refusal(PayrollFrameworkResult.Invalid(envelopeError));

        var result = await mediator.Send(new ImportPayrollFrameworkPackCommand(
            envelope.PackJson, envelope.Code, envelope.EffectiveFrom, envelope.TargetDraftId, PayProfileChangeHttp.RequireUserId(User)), ct);
        return result.Success
            ? CreatedAtAction(nameof(Get), new { id = result.FrameworkId }, PayrollFrameworkHttp.Body(result))
            : PayrollFrameworkHttp.Refusal(result);
    }

    /// <summary>The version as a legal pack (a JSON download), to edit and import again.</summary>
    [HttpGet("{id:guid}/export")]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var export = await mediator.Send(new ExportPayrollFrameworkQuery(id), ct);
        return export is null
            ? NotFound()
            : File(Encoding.UTF8.GetBytes(export.Json), "application/json", export.FileName);
    }

    [HttpPut("{id:guid}/effective-from")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollMasterData)]
    public async Task<IActionResult> SetEffectiveFrom(Guid id, [FromBody] SetPayrollFrameworkEffectiveFromApiRequest request, CancellationToken ct)
    {
        if (!PayrollFrameworkHttp.TryDate(request.EffectiveFrom, out var effectiveFrom))
            return PayrollFrameworkHttp.Refusal(PayrollFrameworkResult.Invalid(PayrollFrameworkHttp.DateFormat));

        var result = await mediator.Send(new SetPayrollFrameworkEffectiveFromCommand(id, effectiveFrom, PayProfileChangeHttp.RequireUserId(User)), ct);
        return result.Success ? Ok(PayrollFrameworkHttp.Body(result)) : PayrollFrameworkHttp.Refusal(result);
    }
}

/// <summary>The body of <c>POST api/payroll/frameworks</c>. Dates are text (<c>yyyy-MM-dd</c>) so that a bad one is answered in Arabic.</summary>
public sealed class CreatePayrollFrameworkApiRequest
{
    public string? RegimeCode { get; set; }
    public Guid? CloneFromId { get; set; }
    public string? Code { get; set; }
    public string? NameAr { get; set; }
    public string? EffectiveFrom { get; set; }
}

public sealed class SetPayrollFrameworkEffectiveFromApiRequest
{
    public string? EffectiveFrom { get; set; }
}

/// <summary>A framework version as the API shows it: where it stands in the cycle, never who the people are beyond their ids.</summary>
public sealed record PayrollFrameworkDto(
    Guid Id,
    string Code,
    string RegimeCode,
    string NameAr,
    int VersionNumber,
    string Status,
    bool IsActive,
    int HashSchemaVersion,
    string? ContentHash,
    string EffectiveFrom,
    string? EffectiveTo,
    Guid? ApprovedBy,
    Guid? PublishedBy)
{
    public static PayrollFrameworkDto From(PayrollConfiguration c) => new(
        c.Id, c.Code, c.RegimeCode, c.NameAr, c.VersionNumber, c.PublicationStatus.ToString(), c.IsActive, c.HashSchemaVersion, c.ContentHash,
        c.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        c.EffectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        c.ApprovedBy, c.PublishedBy);
}

/// <summary>PLAN-54: what the framework controllers share: the one mapping from a command's answer to HTTP, and reading the import's body.</summary>
internal static class PayrollFrameworkHttp
{
    public const string DateFormat = "تاريخ السريان بصيغة yyyy-MM-dd";
    public const string EnvelopeInvalid = "جسم الطلب يجب أن يكون كائن JSON فيه pack (الحزمة)، واختيارياً code وeffectiveFrom وtargetDraftId";

    /// <summary>
    /// The answer to a refusal: 400 a broken rule, 409 a state conflict, 404 no such version. The body carries the Arabic message, the
    /// version's unchanged status, and each error of a refused pack (the JSON path of the field and the row, and what is required).
    /// </summary>
    public static ObjectResult Refusal(PayrollFrameworkResult result) =>
        new(new
        {
            error = result.Error,
            frameworkId = result.FrameworkId,
            status = result.Status?.ToString(),
            errors = result.Errors.Select(e => new { path = e.Path, message = e.Message }),
        })
        { StatusCode = result.StatusCode };

    public static object Body(PayrollFrameworkResult result) => new
    {
        frameworkId = result.FrameworkId,
        code = result.Code,
        versionNumber = result.VersionNumber,
        status = result.Status?.ToString(),
        contentHash = result.ContentHash,
    };

    /// <summary>An absent date is no date; a present one must be <c>yyyy-MM-dd</c>.</summary>
    public static bool TryDate(string? text, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!PayrollFrameworkPackValidator.TryParseDate(text.Trim(), out var parsed))
            return false;
        date = parsed;
        return true;
    }

    public sealed record ImportEnvelope(string PackJson, string? Code, DateOnly? EffectiveFrom, Guid? TargetDraftId);

    public static bool TryReadImportEnvelope(string? body, out ImportEnvelope envelope, out string error)
    {
        envelope = new ImportEnvelope(string.Empty, null, null, null);
        error = EnvelopeInvalid;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            // The pack sits one level down, so the envelope may be one level deeper than a pack.
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = PayrollFrameworkPackReader.MaxDepth + 1 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            string? packJson = null;
            string? code = null;
            DateOnly? effectiveFrom = null;
            Guid? targetDraftId = null;
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "pack" when property.Value.ValueKind == JsonValueKind.Object:
                        packJson = property.Value.GetRawText();
                        break;
                    case "code" when property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null:
                        code = string.IsNullOrWhiteSpace(property.Value.GetString()) ? null : property.Value.GetString()!.Trim();
                        break;
                    case "effectiveFrom" when property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null:
                        if (!TryDate(property.Value.GetString(), out effectiveFrom))
                        {
                            error = DateFormat;
                            return false;
                        }

                        break;
                    case "targetDraftId" when property.Value.ValueKind == JsonValueKind.Null:
                        break;
                    case "targetDraftId" when property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetGuid(out var target):
                        targetDraftId = target;
                        break;
                    default:
                        return false;
                }
            }

            if (packJson is null)
                return false;
            envelope = new ImportEnvelope(packJson, code, effectiveFrom, targetDraftId);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
