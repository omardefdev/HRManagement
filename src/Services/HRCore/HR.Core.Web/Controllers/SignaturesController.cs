using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults.Signatures;
using HRManagement.SharedKernel.Signatures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

/// <summary>Legal digital signature management (SIG-001).</summary>
public class SignaturesController(HRCoreDbContext db, ISignatureService signatureService) : Controller
{
    public async Task<IActionResult> Index(string? subjectType = null, string? subjectId = null)
    {
        var query = db.SignatureRecords.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(subjectType))
            query = query.Where(s => s.SubjectType == subjectType);
        if (!string.IsNullOrWhiteSpace(subjectId))
            query = query.Where(s => s.SubjectId == subjectId);

        var records = await query
            .OrderByDescending(s => s.OccurredAtUtc)
            .Take(200)
            .ToListAsync();

        ViewBag.SubjectType = subjectType;
        ViewBag.SubjectId = subjectId;
        return View(records);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var record = await db.SignatureRecords.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (record is null) return NotFound();
        return View(record);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(Guid id)
    {
        var record = await db.SignatureRecords.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (record is null) return NotFound();

        var valid = signatureService.Verify(record);
        TempData[valid ? "Success" : "Error"] = valid
            ? "التوقيع صالح — التحقق ناجح"
            : "التوقيع غير صالح — فشل التحقق";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Policy(string subjectType)
    {
        var policy = SignaturePolicy.GetPolicy(subjectType);
        if (policy is null) return NotFound();

        var sigCount = await db.SignatureRecords.AsNoTracking()
            .CountAsync(s => s.SubjectType == subjectType);

        ViewBag.Policy = policy;
        ViewBag.TotalSignatures = sigCount;
        return View();
    }
}
