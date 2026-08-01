using HR.Training.Domain.Entities;
using HR.Training.Domain.Enums;
using HR.Training.Domain.Services;
using HR.Training.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HR.Training.Web.Controllers;

[Authorize(Policy = HrManagementPolicies.ModuleEss)]
[Route("[controller]")]
public class EssLearningController(TrainingDbContext db, IPublishEndpoint publish) : Controller
{
    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        var courses = await db.ELearningCourses
            .Where(c => c.IsPublished)
            .OrderBy(c => c.TitleAr)
            .AsNoTracking()
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync();

        ViewBag.EnrolledCourseIds = employeeId is null
            ? new List<Guid>()
            : await db.LearningEnrollments
                .Where(e => e.EmployeeId == employeeId.Value)
                .Select(e => e.ELearningCourseId)
                .ToListAsync();

        return View(courses);
    }

    [HttpPost("Enroll")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enroll(Guid courseId)
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var course = await db.ELearningCourses.FindAsync(courseId);
        if (course is null || !course.IsPublished)
        {
            TempData["Error"] = "الدورة غير متاحة";
            return RedirectToAction(nameof(Index));
        }

        var exists = await db.LearningEnrollments
            .AnyAsync(e => e.ELearningCourseId == courseId && e.EmployeeId == employeeId.Value);
        if (!exists)
        {
            db.LearningEnrollments.Add(new LearningEnrollment
            {
                ELearningCourseId = courseId,
                EmployeeId = employeeId.Value,
                Status = LearningEnrollmentStatus.Enrolled
            });
            await db.SaveChangesAsync();
            TempData["Success"] = "تم الالتحاق بالدورة";
        }
        return RedirectToAction(nameof(MyLearning));
    }

    [HttpGet("MyLearning")]
    public async Task<IActionResult> MyLearning()
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var enrollments = await db.LearningEnrollments
            .Include(e => e.ELearningCourse)
            .Include(e => e.Certificate)
            .Where(e => e.EmployeeId == employeeId.Value)
            .OrderByDescending(e => e.EnrolledAt)
            .Take(SearchFilterBase.MaxPageSize)
            .AsNoTracking()
            .ToListAsync();
        return View(enrollments);
    }

    [HttpPost("UpdateProgress")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProgress(Guid enrollmentId, int progressPercent)
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var enrollment = await db.LearningEnrollments
            .Include(e => e.ELearningCourse)
            .Include(e => e.Certificate)
            .FirstOrDefaultAsync(e => e.Id == enrollmentId && e.EmployeeId == employeeId.Value);
        if (enrollment is null) return NotFound();

        enrollment.ProgressPercent = LearningCompletionRules.ClampProgress(progressPercent);
        enrollment.Status = LearningCompletionRules.StatusFor(enrollment.ProgressPercent);

        if (LearningCompletionRules.IsComplete(enrollment.ProgressPercent))
        {
            enrollment.CompletedAt ??= DateTime.UtcNow;
            if (enrollment.Certificate is null)
            {
                var completedOn = DateOnly.FromDateTime(enrollment.CompletedAt!.Value);
                db.LearningCertificates.Add(new LearningCertificate
                {
                    LearningEnrollmentId = enrollment.Id,
                    CertificateNo = LearningCompletionRules.GenerateCertificateNumber(
                        enrollment.ELearningCourse?.Code ?? "CRS", enrollment.EmployeeId, completedOn),
                    IssuedAt = enrollment.CompletedAt!.Value
                });
                TempData["Success"] = "أكملت الدورة وتم إصدار شهادتك";
            }

            await publish.Publish<ITrainingCompleted>(new
            {
                EmployeeId = enrollment.EmployeeId,
                CourseId = enrollment.ELearningCourseId,
                Score = 100m,
                CertificateFileId = (Guid?)null,
                CompletedAt = enrollment.CompletedAt!.Value,
                CourseTitleAr = enrollment.ELearningCourse?.TitleAr,
                CourseCode = enrollment.ELearningCourse?.Code,
                SourceSystem = "HR.Training.EssLearning",
                SchemaVersion = 3
            });
        }
        else
        {
            enrollment.CompletedAt = null;
        }

        await db.SaveChangesAsync();
        TempData["Success"] ??= "تم تحديث نسبة الإنجاز";
        return RedirectToAction(nameof(MyLearning));
    }

    [HttpGet("Certificate/{enrollmentId}")]
    public async Task<IActionResult> Certificate(Guid enrollmentId)
    {
        var employeeId = HrOrgScopeVisibility.GetEmployeeId(User);
        if (employeeId is null) return Forbid();

        var enrollment = await db.LearningEnrollments
            .Include(e => e.ELearningCourse)
            .Include(e => e.Certificate)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == enrollmentId && e.EmployeeId == employeeId.Value);
        if (enrollment?.Certificate is null) return NotFound();
        return View(enrollment);
    }
}
