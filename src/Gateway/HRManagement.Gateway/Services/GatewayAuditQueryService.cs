using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Gateway.Services;

public interface IGatewayAuditQueryService
{
    Task<GatewayAuditSearchResponse> SearchAsync(GatewayAuditSearchRequest request, CancellationToken cancellationToken);
}

public sealed class GatewayAuditQueryService(GatewayAuditDbContext db) : IGatewayAuditQueryService
{
    public async Task<GatewayAuditSearchResponse> SearchAsync(GatewayAuditSearchRequest request, CancellationToken cancellationToken)
    {
        var pageSize = SearchFilterBase.ClampPageSize(request.PageSize, SearchFilterBase.GatewayAuditMaxPageSize);
        var page = SearchFilterBase.ClampPage(request.Page);

        if (!TryResolveTimeRange(request.TimeRange, out var since, out var rangeError))
        {
            return new GatewayAuditSearchResponse
            {
                Items = Array.Empty<GatewayAuditRowViewModel>(),
                TotalCount = 0,
                Page = page,
                PageSize = pageSize,
                Count401 = 0,
                Count403 = 0,
                Count502 = 0,
                Error = rangeError
            };
        }

        var query = db.AuditEntries.AsNoTracking().Where(x => x.OccurredAtUtc >= since);
        if (request.StatusCode is int code)
            query = query.Where(x => x.StatusCode == code);
        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            var corr = request.CorrelationId.Trim();
            query = corr.Length >= 24
                ? query.Where(x => x.CorrelationId.StartsWith(corr))
                : query.Where(x => x.CorrelationId.Contains(corr));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var counts = await query.GroupBy(_ => 1).Select(g => new
        {
            C401 = g.Count(x => x.StatusCode == 401),
            C403 = g.Count(x => x.StatusCode == 403),
            C502 = g.Count(x => x.StatusCode == 502)
        }).FirstOrDefaultAsync(cancellationToken);

        IQueryable<GatewayAuditEntry> pageQuery = query
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id);

        if (!string.IsNullOrWhiteSpace(request.Cursor)
            && KeysetCursor.TryDecodeLong(request.Cursor, out var cursorAt, out var cursorId))
        {
            pageQuery = pageQuery.Where(x =>
                x.OccurredAtUtc < cursorAt
                || (x.OccurredAtUtc == cursorAt && x.Id < cursorId));
        }
        else
        {
            pageQuery = pageQuery.Skip((page - 1) * pageSize);
        }

        var rows = await pageQuery
            .Take(pageSize + 1)
            .Select(x => new GatewayAuditRowViewModel
            {
                Id = x.Id,
                OccurredAtUtc = x.OccurredAtUtc,
                Decision = x.Decision,
                StatusCode = x.StatusCode,
                Method = x.Method,
                Path = x.Path,
                UserName = x.UserName,
                CorrelationId = x.CorrelationId,
                ElapsedMs = x.ElapsedMs
            })
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        if (hasMore)
            rows = rows.Take(pageSize).ToList();

        string? nextCursor = null;
        if (hasMore && rows.Count > 0)
        {
            var last = rows[^1];
            nextCursor = KeysetCursor.Encode(last.OccurredAtUtc, last.Id);
        }

        return new GatewayAuditSearchResponse
        {
            Items = rows,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Count401 = counts?.C401 ?? 0,
            Count403 = counts?.C403 ?? 0,
            Count502 = counts?.C502 ?? 0,
            NextCursor = nextCursor,
            HasMore = hasMore
        };
    }

    /// <summary>PERF-002: default 24h; reject unbounded "all".</summary>
    internal static bool TryResolveTimeRange(string? timeRange, out DateTimeOffset since, out string? error)
    {
        var now = DateTimeOffset.UtcNow;
        var key = string.IsNullOrWhiteSpace(timeRange) ? "24h" : timeRange.Trim().ToLowerInvariant();
        switch (key)
        {
            case "1h":
                since = now.AddHours(-1);
                error = null;
                return true;
            case "24h":
                since = now.AddHours(-24);
                error = null;
                return true;
            case "7d":
                since = now.AddDays(-7);
                error = null;
                return true;
            case "all":
                since = default;
                error = "نطاق «الكل» غير مسموح. اختر 1 ساعة أو 24 ساعة أو 7 أيام (PERF-002).";
                return false;
            default:
                since = now.AddHours(-24);
                error = null;
                return true;
        }
    }
}
