using HR.Training.Domain.Entities;
using HR.Training.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Training.Web.Controllers;

public class ELearningCoursesController(TrainingDbContext db) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await db.ELearningCourses.AsNoTracking()
            .Include(c => c.Enrollments)
            .OrderBy(c => c.Code)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());

    public IActionResult Create() => View(new ELearningCourse());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ELearningCourse model)
    {
        if (!ModelState.IsValid) return View(model);
        db.ELearningCourses.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء الدورة الإلكترونية";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var course = await db.ELearningCourses.FindAsync(id);
        if (course == null) return NotFound();
        return View(course);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ELearningCourse model)
    {
        if (id != model.Id) return BadRequest();
        var course = await db.ELearningCourses.FindAsync(id);
        if (course == null) return NotFound();
        if (!ModelState.IsValid) return View(model);
        course.Code = model.Code;
        course.TitleAr = model.TitleAr;
        course.Description = model.Description;
        course.Category = model.Category;
        course.EstimatedHours = model.EstimatedHours;
        course.IsPublished = model.IsPublished;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تحديث الدورة";
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var course = await db.ELearningCourses
            .Include(c => c.Modules).ThenInclude(m => m.Lessons)
            .Include(c => c.Enrollments)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);
        if (course == null) return NotFound();
        return View(course);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddModule(Guid courseId, int order, string title)
    {
        var course = await db.ELearningCourses.FindAsync(courseId);
        if (course == null) return NotFound();
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "عنوان الوحدة مطلوب";
            return RedirectToAction(nameof(Details), new { id = courseId });
        }
        db.CourseModules.Add(new CourseModule { ELearningCourseId = courseId, Order = order, Title = title });
        await db.SaveChangesAsync();
        TempData["Success"] = "تمت إضافة الوحدة";
        return RedirectToAction(nameof(Details), new { id = courseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLesson(Guid courseId, Guid moduleId, int order, string title, string? content, Guid? contentFileId, string? externalResourceUrl)
    {
        var module = await db.CourseModules.FindAsync(moduleId);
        if (module == null) return NotFound();
        if (string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "عنوان الدرس مطلوب";
            return RedirectToAction(nameof(Details), new { id = courseId });
        }
        db.Lessons.Add(new Lesson { CourseModuleId = moduleId, Order = order, Title = title, Content = content, ContentFileId = contentFileId, ExternalResourceUrl = externalResourceUrl });
        await db.SaveChangesAsync();
        TempData["Success"] = "تمت إضافة الدرس";
        return RedirectToAction(nameof(Details), new { id = courseId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePublish(Guid id)
    {
        var course = await db.ELearningCourses.FindAsync(id);
        if (course == null) return NotFound();
        course.IsPublished = !course.IsPublished;
        await db.SaveChangesAsync();
        TempData["Success"] = course.IsPublished ? "تم نشر الدورة" : "تم إلغاء نشر الدورة";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var course = await db.ELearningCourses.FindAsync(id);
        if (course == null) return NotFound();
        db.ELearningCourses.Remove(course);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم حذف الدورة";
        return RedirectToAction(nameof(Index));
    }
}
