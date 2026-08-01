using Auth.Directory.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auth.Directory.Web.Controllers;

[Route("api/stats")]
[ApiController]
[Authorize]
public class StatsApiController(AuthDirectoryDbContext db) : ControllerBase
{
    [HttpGet("active-users")]
    public async Task<IActionResult> ActiveUsers(CancellationToken ct)
    {
        var count = await db.AuthUsers.CountAsync(u => u.IsActive, ct);
        return Ok(new { count });
    }
}
