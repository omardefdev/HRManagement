using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

namespace Payroll.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModulePayroll)]
public class PayrollConfigurationsController(PayrollDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.PayrollConfigurations
            .AsNoTracking()
            .Include(c => c.TaxBrackets)
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.Code)
            .ThenByDescending(c => c.VersionNumber)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets.OrderBy(t => t.SortOrder))
            .FirstOrDefaultAsync(c => c.Id == id);
        if (config is null) return NotFound();

        ViewBag.Holidays = await db.OfficialHolidays
            .Where(h => h.PayrollConfigurationId == id)
            .OrderBy(h => h.Date)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        return View(config);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .FirstOrDefaultAsync(c => c.Id == id);
        return config is null ? NotFound() : View(config);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollProcess)]
    public async Task<IActionResult> Edit(Guid id, PayrollConfiguration model)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (config is null) return NotFound();

        if (!ModelState.IsValid) return View(model);

        void ApplyFields(PayrollConfiguration target)
        {
            target.NameAr = model.NameAr;
            target.ProrationDaysBasis = model.ProrationDaysBasis;
            target.DailyRateDivisor = model.DailyRateDivisor;
            target.MissionAllowanceAmount = model.MissionAllowanceAmount;
            target.SecondmentAllowancePercent = model.SecondmentAllowancePercent;
            target.EnableTrainingPremiums = model.EnableTrainingPremiums;
            target.TrainingPremiumAmount = model.TrainingPremiumAmount;
            target.MaxRetroCorrectionMonths = model.MaxRetroCorrectionMonths;
            target.EosGratuityRateFirstPeriod = model.EosGratuityRateFirstPeriod;
            target.EosGratuityFirstYearsThreshold = model.EosGratuityFirstYearsThreshold;
            target.EosGratuityRateAfterThreshold = model.EosGratuityRateAfterThreshold;
            target.NonWorkingDays = model.NonWorkingDays;
            target.UseDbHolidaysOnly = model.UseDbHolidaysOnly;
            target.TaxableBaseMode = model.TaxableBaseMode;
            target.TaxExemptionAmount = model.TaxExemptionAmount;
            target.LeavePayoutDailyDivisor = model.LeavePayoutDailyDivisor;
            target.RoundingMode = model.RoundingMode;
            target.DecimalPlaces = model.DecimalPlaces;
            target.CurrencyCode = string.IsNullOrWhiteSpace(model.CurrencyCode) ? "SYP" : model.CurrencyCode.Trim().ToUpperInvariant();
        }

        if (config.IsImmutable)
        {
            var nextVersion = await db.PayrollConfigurations
                .Where(c => c.Code == config.Code)
                .MaxAsync(c => (int?)c.VersionNumber) ?? config.VersionNumber;
            var draft = PayrollConfigurationVersioning.CloneAsDraft(config, nextVersion + 1, ApplyFields);
            db.PayrollConfigurations.Add(draft);
            await db.SaveChangesAsync();
            TempData["Success"] = $"تم إنشاء إصدار مسودة جديد (v{draft.VersionNumber}) — لا تُعدَّل النسخ المنشورة.";
            return RedirectToAction(nameof(Details), new { id = draft.Id });
        }

        var guard = PayrollConfigurationVersioning.GuardImmutableMutation(config);
        if (guard != null)
        {
            TempData["Error"] = guard;
            return RedirectToAction(nameof(Details), new { id });
        }

        ApplyFields(config);
        config.ContentHash = null;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث مسودة الإعدادات القانونية";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Approve(Guid id)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (config is null) return NotFound();

        var actor = ParseUserId(User) ?? Guid.Empty;
        var (ok, error) = PayrollConfigurationVersioning.TryApprove(config, actor);
        if (!ok)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "تم اعتماد النسخة القانونية";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> LegalAttest(Guid id)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (config is null) return NotFound();

        if (config.PublicationStatus is not (PayrollConfigPublicationStatus.Draft
            or PayrollConfigPublicationStatus.Approved))
        {
            TempData["Error"] = "التصديق القانوني للمسودات/المعتمدة فقط قبل النشر.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var actor = ParseUserId(User) ?? Guid.Empty;
        config.ContentHash ??= PayrollConfigurationHashService.ComputeContentHash(config);

        var prior = await db.LegalAttestations
            .Where(a => a.PayrollConfigurationId == id && a.Status == "Active")
            .ToListAsync();
        foreach (var p in prior)
            p.Status = "Voided";

        db.LegalAttestations.Add(PayrollLegalGovernance.CreateAttestation(config, actor));
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل التصديق القانوني الإلكتروني للنسخة.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Publish(Guid id, string? impactSummary)
    {
        var config = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (config is null) return NotFound();

        var sameCode = await db.PayrollConfigurations
            .Where(c => c.Code == config.Code)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();
        var actor = ParseUserId(User) ?? Guid.Empty;

        var attestation = await db.LegalAttestations
            .Where(a => a.PayrollConfigurationId == id && a.Status == "Active")
            .OrderByDescending(a => a.AttestedAt)
            .FirstOrDefaultAsync();

        var (ok, error, notice) = PayrollLegalGovernance.TryPublishWithGovernance(
            config, sameCode, actor, attestation, impactSummary);
        if (!ok)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(Details), new { id });
        }

        if (notice is not null)
            db.PayrollConfigChangeNotices.Add(notice);

        await db.SaveChangesAsync();
        TempData["Success"] = $"تم نشر الإطار القانوني: {config.NameAr} (v{config.VersionNumber}) مع إشعار تغيير.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = HrManagementPolicies.PayrollApprove)]
    public async Task<IActionResult> Activate(Guid id)
    {
        // PAY-002: Activate = publish path for already-approved/published selection among drafts is deprecated;
        // keep as publish-active for Published rows only.
        var target = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (target is null) return NotFound();

        if (target.PublicationStatus != PayrollConfigPublicationStatus.Published)
            return await Publish(id, impactSummary: null);

        var all = await db.PayrollConfigurations.Where(c => c.Code == target.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync();
        foreach (var c in all)
            c.IsActive = c.Id == id;

        await db.SaveChangesAsync();
        TempData["Success"] = $"تم تفعيل الإطار القانوني: {target.NameAr}";
        return RedirectToAction(nameof(Index));
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }
}
