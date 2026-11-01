using System.Diagnostics;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace HRManagement.Gateway.Controllers;

public class HomeController(
    IGatewayModuleCatalog moduleCatalog,
    IGatewayJourneyCatalog journeyCatalog,
    IGatewayHealthProbeService healthProbeService,
    IGatewayAuditQueryService auditQueryService,
    GatewayRequestMetricsStore metricsStore,
    IHttpClientFactory httpClientFactory,
    IAuthorizationService authorizationService,
    IConfiguration configuration,
    GatewayIntegrationEndpoints endpoints,
    ILogger<HomeController> logger) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var modules = moduleCatalog.Modules;
        var moduleAccess = await ResolveModuleAccessAsync(modules);
        var statuses = await healthProbeService.ProbeAsync(
            modules,
            $"{Request.Scheme}://{Request.Host.Value}",
            module => moduleAccess.GetValueOrDefault(module.Slug),
            cancellationToken);
        var list = modules.Select(m => statuses[m.Slug]).ToList();

        var authDirectoryBase = endpoints.AuthDirectoryBaseUrl;
        var hrCoreBase = endpoints.HrCoreBaseUrl;
        var activeUsersCount = await FetchStatAsync(
            $"{authDirectoryBase}/api/stats/active-users", "count", cancellationToken);
        var pendingApprovalsCount = await FetchStatAsync(
            $"{hrCoreBase}/api/stats/pending-approvals", "count", cancellationToken);

        var moduleMetrics = metricsStore.GetModuleStats();
        var moduleStats = moduleMetrics.Select(m => new ModuleRequestStat
        {
            ModuleName = moduleCatalog.FindBySlug(m.ModuleName)?.DisplayNameAr ?? m.ModuleName,
            RequestCount = m.RequestCount,
            ErrorCount = m.ErrorCount,
            AvgLatencyMs = m.AvgLatencyMs,
        }).ToList();

        var vm = new GatewayDashboardViewModel
        {
            Modules = list,
            Summary = new GatewayHealthSummary
            {
                Total = list.Count,
                Healthy = list.Count(x => x.IsHealthy),
                Unauthorized = list.Count(x => !x.IsAuthorized),
                Degraded = list.Count(x => x.IsAuthorized && !x.IsHealthy),
            },
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            ActiveUsersCount = activeUsersCount,
            PendingApprovalsCount = pendingApprovalsCount,
            ModuleStats = moduleStats,
        };
        return View(vm);
    }

    private async Task<int> FetchStatAsync(string url, string jsonProperty, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var accessToken = await HttpContext.GetTokenAsync("access_token");
            if (!string.IsNullOrWhiteSpace(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            GatewayCallerProof.Sign(request, configuration);
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
            return json.TryGetProperty(jsonProperty, out var prop) ? prop.GetInt32() : -1;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch stat from {Url}", url);
            return -1;
        }
    }

    public IActionResult SeparationJourney() =>
        RedirectToAction(nameof(JourneyDetails), new { id = "separation-exit" });

    public IActionResult HireJourney() =>
        RedirectToAction(nameof(JourneyDetails), new { id = "new-hire" });

    [HttpGet]
    public IActionResult Journeys()
    {
        return View(journeyCatalog.Journeys.OrderBy(x => x.Title).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> JourneyDetails(string id)
    {
        var journey = journeyCatalog.FindById(id);
        if (journey is null)
            return NotFound();

        var modules = journey.Steps
            .Select(step => string.IsNullOrWhiteSpace(step.ModuleSlug) ? null : moduleCatalog.FindBySlug(step.ModuleSlug))
            .Where(module => module is not null)
            .Cast<GatewayModule>()
            .DistinctBy(module => module.Slug)
            .ToList();
        var moduleAccess = await ResolveModuleAccessAsync(modules);

        var steps = journey.Steps.Select(step =>
        {
            var module = string.IsNullOrWhiteSpace(step.ModuleSlug)
                ? null
                : moduleCatalog.FindBySlug(step.ModuleSlug);
            var canOpenPublic = GatewayJourneyPublicPaths.TryResolve(step.GatewayPublicPath, out var publicHref);
            return new GatewayJourneyStepStatus
            {
                Step = step,
                Module = module,
                HasModule = module is not null,
                IsAuthorized = module is not null && moduleAccess.GetValueOrDefault(module.Slug),
                CanOpenPublic = canOpenPublic,
                PublicHref = canOpenPublic ? publicHref : null
            };
        }).ToList();

        return View(new GatewayJourneyDetailsViewModel
        {
            Journey = journey,
            Steps = steps
        });
    }

    [HttpGet]
    public async Task<IActionResult> OpenModule(string slug, string? uiPath = null)
    {
        var module = moduleCatalog.FindBySlug(slug);
        if (module is null)
            return NotFound();
        if (!await IsAuthorizedForModuleAsync(module))
            return Forbid();

        var allowedPrefix = module.UiPath.TrimEnd('/') + "/";
        if (!SafeRedirect.TryResolveModuleUiPath(Url, allowedPrefix, uiPath, out var target))
        {
            logger.LogWarning("Rejected open redirect attempt for module {Slug} path {UiPath}", slug, uiPath);
            return BadRequest("مسار غير مسموح.");
        }

        return Redirect(target);
    }

    [HttpGet("/Home/ErrorStatus/{code:int}")]
    public IActionResult ErrorStatus(int code)
    {
        logger.LogWarning("Gateway status page rendered with code {StatusCode}", code);
        Response.StatusCode = code;
        return View("Status", code);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UpstreamHealth(CancellationToken cancellationToken)
    {
        var moduleAccess = await ResolveModuleAccessAsync(moduleCatalog.Modules);
        var statuses = await healthProbeService.ProbeAsync(
            moduleCatalog.Modules,
            $"{Request.Scheme}://{Request.Host.Value}",
            module => moduleAccess.GetValueOrDefault(module.Slug),
            cancellationToken);
        return Json(statuses.Values.Select(x => new
        {
            slug = x.Module.Slug,
            label = x.Module.Name,
            path = x.Module.HealthPath,
            ok = x.IsHealthy,
            allowed = x.IsAuthorized,
            statusCode = x.StatusCode,
            latencyMs = x.LatencyMs,
            error = x.Error
        }));
    }

    [HttpGet]
    public async Task<IActionResult> Operations(string? correlationId, CancellationToken cancellationToken)
    {
        var moduleAccess = await ResolveModuleAccessAsync(moduleCatalog.Modules);
        var statuses = await healthProbeService.ProbeAsync(
            moduleCatalog.Modules,
            $"{Request.Scheme}://{Request.Host.Value}",
            module => moduleAccess.GetValueOrDefault(module.Slug),
            cancellationToken);
        var ordered = statuses.Values.OrderBy(x => x.Module.Name).ToList();
        ViewData["Metrics"] = metricsStore.Snapshot();
        ViewData["CorrelationIdFilter"] = correlationId;
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            var audit = await auditQueryService.SearchAsync(new GatewayAuditSearchRequest
            {
                CorrelationId = correlationId,
                TimeRange = "7d",
                Page = 1,
                PageSize = 25
            }, cancellationToken);
            ViewData["AuditTrace"] = audit.Items;
        }
        return View(ordered);
    }

    [HttpGet("/ops/metrics-summary")]
    public IActionResult MetricsSummary()
    {
        var snapshot = metricsStore.Snapshot();
        return Json(new
        {
            statusCounters = snapshot.StatusCounters,
            p95 = snapshot.ModuleP95LatencyMs,
            recentErrors = snapshot.RecentErrors
        });
    }

    [HttpGet]
    public async Task<IActionResult> Audit(
        int? statusCode,
        string? correlationId,
        string? timeRange = "24h",
        int page = 1,
        int pageSize = SearchFilterBase.GatewayAuditMaxPageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = SearchFilterBase.ClampPageSize(pageSize, SearchFilterBase.GatewayAuditMaxPageSize);
        page = SearchFilterBase.ClampPage(page);
        var request = new GatewayAuditSearchRequest
        {
            StatusCode = statusCode,
            CorrelationId = correlationId,
            TimeRange = timeRange,
            Page = page,
            PageSize = pageSize
        };
        var result = await auditQueryService.SearchAsync(request, cancellationToken);
        ViewData["Request"] = request;
        return View(result);
    }

    [HttpGet("/ops/audit-search")]
    public async Task<IActionResult> AuditSearch(
        int? statusCode,
        string? correlationId,
        string? timeRange = "24h",
        int page = 1,
        int pageSize = SearchFilterBase.GatewayAuditMaxPageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = SearchFilterBase.ClampPageSize(pageSize, SearchFilterBase.GatewayAuditMaxPageSize);
        page = SearchFilterBase.ClampPage(page);
        var request = new GatewayAuditSearchRequest
        {
            StatusCode = statusCode,
            CorrelationId = correlationId,
            TimeRange = timeRange,
            Page = page,
            PageSize = pageSize
        };
        var result = await auditQueryService.SearchAsync(request, cancellationToken);
        return Json(result);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewData["RequestId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View();
    }

    private async Task<bool> IsAuthorizedForModuleAsync(GatewayModule module)
    {
        var result = await authorizationService.AuthorizeAsync(User, resource: null, module.Policy);
        return result.Succeeded;
    }

    private async Task<Dictionary<string, bool>> ResolveModuleAccessAsync(IEnumerable<GatewayModule> modules)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
            result[module.Slug] = await IsAuthorizedForModuleAsync(module);
        return result;
    }
}
