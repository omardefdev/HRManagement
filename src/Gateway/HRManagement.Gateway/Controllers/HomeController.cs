using System.Diagnostics;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Gateway.Controllers;

public class HomeController(
    IGatewayModuleCatalog moduleCatalog,
    IGatewayJourneyCatalog journeyCatalog,
    IGatewayHealthProbeService healthProbeService,
    IGatewayAuditQueryService auditQueryService,
    GatewayRequestMetricsStore metricsStore,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<HomeController> logger) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var modules = moduleCatalog.Modules;
        var statuses = await healthProbeService.ProbeAsync(
            modules,
            $"{Request.Scheme}://{Request.Host.Value}",
            IsAuthorizedForModule,
            cancellationToken);
        var list = modules.Select(m => statuses[m.Slug]).ToList();

        var authDirectoryBase = configuration["Integration:AuthDirectoryBaseUrl"] ?? "http://localhost:5130";
        var hrCoreBase = configuration["Integration:HrCoreBaseUrl"] ?? "http://localhost:5010";
        var activeUsersCount = await FetchStatAsync(
            $"{authDirectoryBase}/api/stats/active-users", "count", cancellationToken);
        var pendingApprovalsCount = await FetchStatAsync(
            $"{hrCoreBase}/api/stats/pending-approvals", "count", cancellationToken);

        var moduleMetrics = metricsStore.GetModuleStats();
        var moduleStats = moduleMetrics.Select(m => new ModuleRequestStat
        {
            ModuleName = m.ModuleName,
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
            var json = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(url, ct);
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
    public IActionResult JourneyDetails(string id)
    {
        var journey = journeyCatalog.FindById(id);
        if (journey is null)
            return NotFound();

        var steps = journey.Steps.Select(step =>
        {
            var module = string.IsNullOrWhiteSpace(step.ModuleSlug)
                ? null
                : moduleCatalog.FindBySlug(step.ModuleSlug);
            return new GatewayJourneyStepStatus
            {
                Step = step,
                Module = module,
                HasModule = module is not null,
                IsAuthorized = module is not null && IsAuthorizedForModule(module)
            };
        }).ToList();

        return View(new GatewayJourneyDetailsViewModel
        {
            Journey = journey,
            Steps = steps
        });
    }

    [HttpGet]
    public IActionResult OpenModule(string slug, string? uiPath = null)
    {
        var module = moduleCatalog.FindBySlug(slug);
        if (module is null)
            return NotFound();
        if (!IsAuthorizedForModule(module))
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
        var statuses = await healthProbeService.ProbeAsync(
            moduleCatalog.Modules,
            $"{Request.Scheme}://{Request.Host.Value}",
            IsAuthorizedForModule,
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
        var statuses = await healthProbeService.ProbeAsync(
            moduleCatalog.Modules,
            $"{Request.Scheme}://{Request.Host.Value}",
            IsAuthorizedForModule,
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

    private bool IsAuthorizedForModule(GatewayModule module)
    {
        if (User.IsInRole(HrManagementRoles.Admin))
            return true;
        return User.IsInRole(module.RequiredRole);
    }
}
