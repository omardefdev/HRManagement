using Dev.Analytics.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dev.Analytics.Web.Controllers;

[ApiController]
[Route("api/analytics/events")]
public class AnalyticsEventsApiController(AnalyticsDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 200, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 2000);
        var rows = await db.AnalyticsEvents.AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .Take(take)
            .Select(e => new { e.Id, e.EventName, e.Payload, e.OccurredAt })
            .ToListAsync(cancellationToken);
        return Ok(rows);
    }
}
