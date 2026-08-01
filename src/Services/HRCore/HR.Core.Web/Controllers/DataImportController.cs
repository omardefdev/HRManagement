using System.Globalization;
using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.HrCoreWrite)]
public class DataImportController(HRCoreDbContext db, EmployeeIntegrationService integration) : Controller
{
    public IActionResult Index() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportOrgUnits(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "الملف فارغ";
            return RedirectToAction(nameof(Index));
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var header = await reader.ReadLineAsync();
        var count = 0;
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var parts = line.Split(',');
            if (parts.Length < 2) continue;
            var code = parts[0].Trim();
            var name = parts[1].Trim();
            if (await db.OrgUnits.AnyAsync(o => o.Code == code)) continue;
            db.OrgUnits.Add(new OrgUnit { Code = code, NameAr = name });
            count++;
        }

        await db.SaveChangesAsync();
        TempData["Success"] = $"تم استيراد {count} وحدة تنظيمية";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportEmployees(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "الملف فارغ";
            return RedirectToAction(nameof(Index));
        }

        using var reader = new StreamReader(file.OpenReadStream());
        await reader.ReadLineAsync();
        var count = 0;
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var parts = line.Split(',');
            if (parts.Length < 5) continue;
            var nationalId = parts[0].Trim();
            if (await db.Employees.AnyAsync(e => e.NationalId == nationalId)) continue;

            var orgCode = parts[4].Trim();
            var org = await db.OrgUnits.FirstOrDefaultAsync(o => o.Code == orgCode);

            var employee = new Employee
            {
                NationalId = nationalId,
                FirstName = parts[1].Trim(),
                LastName = parts[2].Trim(),
                FullName = $"{parts[1].Trim()} {parts[2].Trim()}",
                BirthDate = DateTime.Parse(parts[3].Trim(), CultureInfo.InvariantCulture),
                Gender = Gender.Male,
                PrimaryOrgUnitId = org?.Id,
                HireDate = DateTime.UtcNow,
                CurrentStatus = EmploymentStatus.Active
            };

            await integration.CreateEmployeeWithEventAsync(employee);
            count++;
        }

        TempData["Success"] = $"تم استيراد {count} موظف";
        return RedirectToAction(nameof(Index));
    }
}
