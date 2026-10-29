using System.Reflection;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Services;

/// <summary>What the controller's <c>id</c> route value identifies; <see cref="CentralOnly"/> closes the controller to branch users.</summary>
public enum RecruitmentScopeKind
{
    Requisition,
    Application,
    Offer,
    CommitteeSession,
    CandidateDocument,
    PublicPosting,
    CentralOnly
}

/// <summary>
/// PLAN-25 (review finding 4): on an action whose <c>id</c> is not the controller's record (e.g. a candidate document
/// download on RecruitmentApplications), names the kind <c>id</c> resolves to.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RecruitmentScopeIdAttribute(RecruitmentScopeKind kind) : Attribute
{
    public RecruitmentScopeKind Kind { get; } = kind;
}

/// <summary>
/// PLAN-25 (D10, decision 8): org scope on the recruitment screens for branch-scoped users (<see cref="OrgAccessScope.MustFilter"/>).
/// Every action argument that identifies a recruitment record — the <c>id</c> route value (of the controller's kind) and
/// <c>requisitionId</c> / <c>applicationId</c> / <c>offerId</c> / <c>sessionId</c> arguments or properties of posted forms —
/// must resolve to an org unit inside the user's scope; otherwise the request ends in 404 (details) — the same for a POST,
/// so a foreign id is indistinguishable from a missing one. Records with no org unit (legacy applications without a
/// requisition) are central-only. Unrestricted users are not queried at all. List pages filter their own queries
/// (<see cref="RecruitmentOrgScopeQueries"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RecruitmentOrgScopeAttribute : TypeFilterAttribute
{
    public RecruitmentOrgScopeAttribute(RecruitmentScopeKind idKind) : base(typeof(RecruitmentOrgScopeFilter)) =>
        Arguments = [idKind];
}

public sealed class RecruitmentOrgScopeFilter(RecruitmentScopeKind idKind, OrgScopeService orgScope, HRCoreDbContext db)
    : IAsyncActionFilter
{
    private static readonly Dictionary<string, RecruitmentScopeKind> NamedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["requisitionId"] = RecruitmentScopeKind.Requisition,
        ["applicationId"] = RecruitmentScopeKind.Application,
        ["recruitmentApplicationId"] = RecruitmentScopeKind.Application,
        ["offerId"] = RecruitmentScopeKind.Offer,
        ["sessionId"] = RecruitmentScopeKind.CommitteeSession,
        ["postingId"] = RecruitmentScopeKind.PublicPosting
    };

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var scope = await orgScope.GetAllowedOrgUnitIdsAsync(context.HttpContext.User, context.HttpContext.RequestAborted);
        if (!scope.MustFilter)
        {
            await next();
            return;
        }

        if (scope.IsDenied || idKind == RecruitmentScopeKind.CentralOnly)
        {
            context.Result = new ForbidResult();
            return;
        }

        var routeIdKind = context.ActionDescriptor.EndpointMetadata.OfType<RecruitmentScopeIdAttribute>().FirstOrDefault()?.Kind ?? idKind;
        foreach (var (kind, id) in CollectIds(context.ActionArguments, routeIdKind))
        {
            var (found, orgUnitId) = await ResolveOrgUnitAsync(kind, id, context.HttpContext.RequestAborted);
            if (found && (orgUnitId is null || !scope.CanAccess(orgUnitId.Value)))
            {
                context.Result = new NotFoundResult();
                return;
            }
        }

        await next();
    }

    private static IEnumerable<(RecruitmentScopeKind Kind, Guid Id)> CollectIds(IDictionary<string, object?> arguments, RecruitmentScopeKind routeIdKind)
    {
        foreach (var (name, value) in arguments)
        {
            if (value is Guid guid)
            {
                if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
                    yield return (routeIdKind, guid);
                else if (NamedIds.TryGetValue(name, out var kind))
                    yield return (kind, guid);
                continue;
            }

            if (value is null || value.GetType().IsPrimitive || value is string || value is Microsoft.AspNetCore.Http.IFormFile)
                continue;

            foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!NamedIds.TryGetValue(property.Name, out var kind)
                    || (property.PropertyType != typeof(Guid) && property.PropertyType != typeof(Guid?)))
                    continue;
                if (property.GetValue(value) is Guid propertyId && propertyId != Guid.Empty)
                    yield return (kind, propertyId);
            }
        }
    }

    private async Task<(bool Found, Guid? OrgUnitId)> ResolveOrgUnitAsync(RecruitmentScopeKind kind, Guid id, CancellationToken ct)
    {
        var rows = kind switch
        {
            RecruitmentScopeKind.Requisition => db.RecruitmentRequisitions.AsNoTracking()
                .Where(r => r.Id == id).Select(r => new OrgUnitRow((Guid?)r.FundedPosition!.OrgUnitId)),
            RecruitmentScopeKind.Application => db.RecruitmentApplications.AsNoTracking()
                .Where(a => a.Id == id).Select(a => new OrgUnitRow((Guid?)a.Requisition!.FundedPosition!.OrgUnitId)),
            RecruitmentScopeKind.Offer => db.OfferLetters.AsNoTracking()
                .Where(o => o.Id == id).Select(o => new OrgUnitRow((Guid?)o.FundedPosition!.OrgUnitId)),
            RecruitmentScopeKind.CommitteeSession => db.SelectionCommitteeSessions.AsNoTracking()
                .Where(s => s.Id == id).Select(s => new OrgUnitRow((Guid?)s.Requisition!.FundedPosition!.OrgUnitId)),
            RecruitmentScopeKind.CandidateDocument => db.CandidateDocuments.AsNoTracking()
                .Where(d => d.Id == id)
                .Join(db.RecruitmentApplications, d => d.RecruitmentApplicationId, a => a.Id,
                    (_, a) => new OrgUnitRow((Guid?)a.Requisition!.FundedPosition!.OrgUnitId)),
            RecruitmentScopeKind.PublicPosting => db.PublicJobPostings.AsNoTracking()
                .Where(p => p.Id == id).Select(p => new OrgUnitRow((Guid?)p.Requisition!.FundedPosition!.OrgUnitId)),
            _ => null
        };
        if (rows is null)
            return (false, null);

        var row = await rows.FirstOrDefaultAsync(ct);
        return row is null ? (false, null) : (true, row.OrgUnitId);
    }

    private sealed record OrgUnitRow(Guid? OrgUnitId);
}

/// <summary>PLAN-25 (D10): list-page filters for branch-scoped users (same rule as <see cref="RecruitmentOrgScopeFilter"/>).</summary>
public static class RecruitmentOrgScopeQueries
{
    /// <summary>The caller's scope; unrestricted when the request has no services (manual controller construction in tests).</summary>
    public static async Task<OrgAccessScope> GetScopeAsync(Microsoft.AspNetCore.Http.HttpContext? http)
    {
        var service = http?.RequestServices?.GetService(typeof(OrgScopeService)) as OrgScopeService;
        return service is null || http is null
            ? OrgAccessScope.Unrestricted()
            : await service.GetAllowedOrgUnitIdsAsync(http.User, http.RequestAborted);
    }

    public static IQueryable<HR.Core.Domain.Entities.RecruitmentRequisition> InScope(
        this IQueryable<HR.Core.Domain.Entities.RecruitmentRequisition> query, OrgAccessScope scope)
    {
        if (!scope.MustFilter)
            return query;
        var allowed = scope.AllowedOrgUnitIds.ToList();
        return query.Where(r => allowed.Contains(r.FundedPosition!.OrgUnitId));
    }

    public static IQueryable<HR.Core.Domain.Entities.RecruitmentApplication> InScope(
        this IQueryable<HR.Core.Domain.Entities.RecruitmentApplication> query, OrgAccessScope scope)
    {
        if (!scope.MustFilter)
            return query;
        var allowed = scope.AllowedOrgUnitIds.ToList();
        return query.Where(a => a.Requisition != null && allowed.Contains(a.Requisition.FundedPosition!.OrgUnitId));
    }

    /// <summary>PLAN-25 (review finding 5): legacy postings without a requisition are central-only.</summary>
    public static IQueryable<HR.Core.Domain.Entities.PublicJobPosting> InScope(
        this IQueryable<HR.Core.Domain.Entities.PublicJobPosting> query, OrgAccessScope scope)
    {
        if (!scope.MustFilter)
            return query;
        var allowed = scope.AllowedOrgUnitIds.ToList();
        return query.Where(p => p.Requisition != null && allowed.Contains(p.Requisition.FundedPosition!.OrgUnitId));
    }

    public static IQueryable<HR.Core.Domain.Entities.OfferLetter> InScope(
        this IQueryable<HR.Core.Domain.Entities.OfferLetter> query, OrgAccessScope scope)
    {
        if (!scope.MustFilter)
            return query;
        var allowed = scope.AllowedOrgUnitIds.ToList();
        return query.Where(o => allowed.Contains(o.FundedPosition!.OrgUnitId));
    }
}
