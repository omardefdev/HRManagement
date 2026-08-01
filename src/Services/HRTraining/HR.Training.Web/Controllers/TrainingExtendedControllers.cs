using HR.Training.Domain.Entities;
using HR.Training.Domain.Enums;
using HR.Training.Domain.Services;
using HR.Training.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.ViewModels;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HR.Training.Web.Controllers;

public class TrainingPlansController(TrainingDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.TrainingPlans.AsNoTracking().OrderByDescending(x => x.Year).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new TrainingPlan { Year = DateTime.UtcNow.Year });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TrainingPlan model)
    {
        if (!ModelState.IsValid) return View(model);
        db.TrainingPlans.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء خطة التدريب";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var p = await db.TrainingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        return View(p);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var p = await db.TrainingPlans.FindAsync(id);
        if (p == null) return NotFound();
        if (p.ApprovedAt != null) { TempData["Error"] = "لا يمكن تعديل خطة معتمدة"; return RedirectToAction(nameof(Details), new { id }); }
        return View(p);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, TrainingPlan model)
    {
        if (id != model.Id) return BadRequest();
        var p = await db.TrainingPlans.FindAsync(id);
        if (p == null) return NotFound();
        if (p.ApprovedAt != null) { TempData["Error"] = "لا يمكن تعديل خطة معتمدة"; return RedirectToAction(nameof(Details), new { id }); }
        if (!ModelState.IsValid) return View(model);
        p.Year = model.Year;
        p.OrgUnitId = model.OrgUnitId;
        p.PlannedCourses = model.PlannedCourses;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث خطة التدريب";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        var p = await db.TrainingPlans.FindAsync(id);
        if (p == null) return NotFound();
        if (p.ApprovedAt != null) { TempData["Error"] = "الخطة معتمدة مسبقاً"; return RedirectToAction(nameof(Details), new { id }); }
        p.ApprovedBy = Guid.Empty;
        p.ApprovedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم اعتماد خطة التدريب";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class QualificationCodesController(TrainingDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.QualificationCodes.AsNoTracking().OrderBy(x => x.Code).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new QualificationCode());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(QualificationCode model)
    {
        if (!ModelState.IsValid) return View(model);
        db.QualificationCodes.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة رمز التأهيل";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var q = await db.QualificationCodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (q == null) return NotFound();
        return View(q);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var q = await db.QualificationCodes.FindAsync(id);
        if (q == null) return NotFound();
        return View(q);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, QualificationCode model)
    {
        if (id != model.Id) return BadRequest();
        var q = await db.QualificationCodes.FindAsync(id);
        if (q == null) return NotFound();
        if (!ModelState.IsValid) return View(model);
        q.Code = model.Code;
        q.TitleAr = model.TitleAr;
        q.TitleEn = model.TitleEn;
        q.RequiredCourses = model.RequiredCourses;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث رمز التأهيل";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var q = await db.QualificationCodes.FindAsync(id);
        if (q == null) return NotFound();
        db.QualificationCodes.Remove(q);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف رمز التأهيل";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> GapAnalysis(Guid qualificationCodeId, Guid employeeId)
    {
        if (await this.ForbidIfEmployeeOutOfScopeAsync(
                User, employeeId, db.EmployeeOrgUnitSnapshots, db.OrgUnitSnapshots) is { } denied)
            return denied;

        var qc = await db.QualificationCodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == qualificationCodeId);
        if (qc == null) return NotFound();

        var completedCourseIds = await db.TrainingEnrollments
            .Where(e => e.EmployeeId == employeeId && e.Status == EnrollmentStatus.Completed)
            .Select(e => e.CourseId)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        var analysis = TrainingGapAnalysis.AnalyzeGap(completedCourseIds, qc.RequiredCourses);

        var allCourses = await db.TrainingCourses.AsNoTracking()
            .Where(c => analysis.RequiredCourseIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.TitleAr);

        ViewBag.QualificationCode = qc;
        ViewBag.EmployeeId = employeeId;
        ViewBag.CourseNames = allCourses;
        return View(analysis);
    }
}

public class TrainingProvidersController(TrainingDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.TrainingProviders.AsNoTracking().OrderBy(x => x.Name).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new TrainingProvider());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TrainingProvider model)
    {
        if (!ModelState.IsValid) return View(model);
        db.TrainingProviders.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة مزود التدريب";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var p = await db.TrainingProviders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound();
        return View(p);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var p = await db.TrainingProviders.FindAsync(id);
        if (p == null) return NotFound();
        return View(p);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, TrainingProvider model)
    {
        if (id != model.Id) return BadRequest();
        var p = await db.TrainingProviders.FindAsync(id);
        if (p == null) return NotFound();
        if (!ModelState.IsValid) return View(model);
        p.Name = model.Name;
        p.ContactInfo = model.ContactInfo;
        p.Country = model.Country;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث مزود التدريب";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var p = await db.TrainingProviders.FindAsync(id);
        if (p == null) return NotFound();
        db.TrainingProviders.Remove(p);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف مزود التدريب";
        return RedirectToAction(nameof(Index));
    }
}

public class TrainingNominationsController(TrainingDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.TrainingNominations.AsNoTracking().Include(n => n.Course).OrderByDescending(x => x.NominatedAt).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Create()
    {
        ViewBag.Courses = new SelectList(await db.TrainingCourses.AsNoTracking().OrderBy(c => c.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TitleAr");
        ViewBag.TrainingPlans = new SelectList(await db.TrainingPlans.AsNoTracking().OrderByDescending(p => p.Year).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Year");
        return View(new TrainingNomination { NominatedAt = DateTime.UtcNow, Status = ApprovalStatus.Pending });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TrainingNomination model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Courses = new SelectList(await db.TrainingCourses.AsNoTracking().OrderBy(c => c.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TitleAr");
            return View(model);
        }
        model.NominatedAt = DateTime.UtcNow;
        model.Status = ApprovalStatus.Pending;
        db.TrainingNominations.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الترشيح";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var n = await db.TrainingNominations.Include(x => x.Course).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (n == null) return NotFound();
        var enrollment = await db.TrainingEnrollments
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == n.EmployeeId && e.CourseId == n.CourseId);
        ViewBag.Enrollment = enrollment;
        return View(n);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        var n = await db.TrainingNominations.FindAsync(id);
        if (n == null) return NotFound();
        if (n.Status != ApprovalStatus.Pending)
        {
            TempData["Error"] = "الترشيح ليس قيد الانتظار";
            return RedirectToAction(nameof(Details), new { id });
        }

        var exists = await db.TrainingEnrollments.AnyAsync(e => e.EmployeeId == n.EmployeeId && e.CourseId == n.CourseId);
        if (exists)
        {
            TempData["Error"] = "الموظف مسجل في هذه الدورة مسبقاً";
            return RedirectToAction(nameof(Details), new { id });
        }

        n.Status = ApprovalStatus.Approved;
        db.TrainingEnrollments.Add(new TrainingEnrollment
        {
            CourseId = n.CourseId,
            EmployeeId = n.EmployeeId,
            EnrolledAt = DateTime.UtcNow,
            Status = EnrollmentStatus.Enrolled
        });

        if (n.TrainingPlanId.HasValue)
        {
            var plan = await db.TrainingPlans.FindAsync(n.TrainingPlanId.Value);
            if (plan is not null)
            {
                var linked = new List<object>();
                if (!string.IsNullOrWhiteSpace(plan.PlannedCourses))
                {
                    try
                    {
                        var existing = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(plan.PlannedCourses);
                        if (existing is not null) linked.AddRange(existing.Cast<object>());
                    }
                    catch { }
                }
                linked.Add(new { nominationId = n.Id, courseId = n.CourseId, employeeId = n.EmployeeId, approvedAt = DateTime.UtcNow });
                plan.PlannedCourses = JsonSerializer.Serialize(linked);
            }
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "تم اعتماد الترشيح وتسجيل الموظف في الدورة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id)
    {
        var n = await db.TrainingNominations.FindAsync(id);
        if (n == null) return NotFound();
        if (n.Status != ApprovalStatus.Pending) { TempData["Error"] = "الترشيح ليس قيد الانتظار"; return RedirectToAction(nameof(Details), new { id }); }
        n.Status = ApprovalStatus.Rejected;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم رفض الترشيح";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class MissionFilesController(TrainingDbContext db, IPublishEndpoint publish) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.MissionFiles.AsNoTracking().OrderByDescending(x => x.StartDate).Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public IActionResult Create() => View(new MissionFile
    {
        StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Status = MissionStatus.Proposed
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MissionFile model)
    {
        if (!ModelState.IsValid) return View(model);
        model.Status = MissionStatus.Proposed;
        db.MissionFiles.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء ملف البعثة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var m = await db.MissionFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound();
        return View(m);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var m = await db.MissionFiles.FindAsync(id);
        if (m == null) return NotFound();
        if (m.Status != MissionStatus.Proposed) { TempData["Error"] = "لا يمكن تعديل بعثة غير مقترحة"; return RedirectToAction(nameof(Details), new { id }); }
        return View(m);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, MissionFile model)
    {
        if (id != model.Id) return BadRequest();
        var m = await db.MissionFiles.FindAsync(id);
        if (m == null) return NotFound();
        if (m.Status != MissionStatus.Proposed) { TempData["Error"] = "لا يمكن تعديل بعثة غير مقترحة"; return RedirectToAction(nameof(Details), new { id }); }
        if (!ModelState.IsValid) return View(model);
        m.EmployeeId = model.EmployeeId;
        m.Destination = model.Destination;
        m.StartDate = model.StartDate;
        m.EndDate = model.EndDate;
        m.Purpose = model.Purpose;
        m.Sponsor = model.Sponsor;
        m.Documents = model.Documents;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث ملف البعثة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        var m = await db.MissionFiles.FindAsync(id);
        if (m == null) return NotFound();
        if (m.Status != MissionStatus.Proposed) { TempData["Error"] = "البعثة ليست مقترحة"; return RedirectToAction(nameof(Details), new { id }); }
        m.Status = MissionStatus.Approved;
        await db.SaveChangesAsync();

        await publish.Publish<IMissionApproved>(new
        {
            MissionFileId = m.Id,
            m.EmployeeId,
            m.Destination,
            StartDate = m.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            EndDate = m.EndDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) ?? new DateTime(DateTime.MaxValue.Ticks, DateTimeKind.Utc),
            CorrelationId = Guid.NewGuid(),
            SourceSystem = "HR.Training"
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم اعتماد البعثة ونشر الحدث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkOngoing(Guid id)
    {
        var m = await db.MissionFiles.FindAsync(id);
        if (m == null) return NotFound();
        if (m.Status != MissionStatus.Approved) { TempData["Error"] = "البعثة ليست معتمدة"; return RedirectToAction(nameof(Details), new { id }); }
        m.Status = MissionStatus.Ongoing;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحويل البعثة إلى جارية";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(Guid id)
    {
        var m = await db.MissionFiles.FindAsync(id);
        if (m == null) return NotFound();
        if (m.Status != MissionStatus.Ongoing) { TempData["Error"] = "البعثة ليست جارية"; return RedirectToAction(nameof(Details), new { id }); }
        m.Status = MissionStatus.Completed;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إكمال البعثة";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class TrainingResultsController(TrainingDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.TrainingResults.AsNoTracking()
            .Include(r => r.Enrollment).ThenInclude(e => e.Course)
            .OrderByDescending(x => x.GradedAt ?? DateTime.MinValue)
            .Take(SearchFilterBase.MaxPageSize).ToListAsync());

    public async Task<IActionResult> Details(Guid id)
    {
        var r = await db.TrainingResults
            .Include(x => x.Enrollment).ThenInclude(e => e.Course)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return NotFound();
        return View(r);
    }
}
