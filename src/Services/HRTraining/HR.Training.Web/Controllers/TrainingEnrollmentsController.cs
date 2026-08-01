using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HR.Training.Domain.Entities;
using HR.Training.Domain.Enums;
using HR.Training.Domain.Services;
using HR.Training.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Training.Web.Controllers;

public class TrainingEnrollmentsController : Controller
{
    private readonly TrainingDbContext _db;
    private readonly IPublishEndpoint _publish;
    private readonly IFileReferenceClient _fileRefs;

    public TrainingEnrollmentsController(TrainingDbContext db, IPublishEndpoint publish, IFileReferenceClient fileRefs)
    {
        _db = db;
        _publish = publish;
        _fileRefs = fileRefs;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _db.TrainingEnrollments
            .Include(e => e.Course)
            .Include(e => e.Result)
            .AsNoTracking()
            .Where(e => e.Status != EnrollmentStatus.Cancelled)
            .OrderByDescending(e => e.EnrolledAt)
            .Take(SearchFilterBase.MaxPageSize)
            .ToListAsync());
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Courses = new SelectList(
            await _db.TrainingCourses.AsNoTracking().OrderBy(c => c.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TitleAr");
        return View(new TrainingEnrollment { EnrolledAt = DateTime.UtcNow, Status = EnrollmentStatus.Enrolled });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TrainingEnrollment model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Courses = new SelectList(await _db.TrainingCourses.AsNoTracking().OrderBy(c => c.Code).Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "TitleAr");
            return View(model);
        }
        model.EnrolledAt = DateTime.UtcNow;
        model.Status = EnrollmentStatus.Enrolled;
        _db.TrainingEnrollments.Add(model);
        await _db.SaveChangesAsync();

        foreach (var step in TrainingChecklistDefaults.CreateFor(model.Id))
            _db.TrainingChecklists.Add(step);
        await _db.SaveChangesAsync();
        var enrolled = await _db.TrainingChecklists.FirstAsync(c =>
            c.EnrollmentId == model.Id && c.Step == TrainingChecklistStep.Enrolled);
        TrainingChecklistDefaults.Mark(enrolled, null, $"enrollment:{model.Id}");
        await _db.SaveChangesAsync();

        TempData["Success"] = "تم تسجيل الموظف في الدورة";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await _db.TrainingEnrollments
            .Include(x => x.Course).ThenInclude(c => c.Provider)
            .Include(x => x.Result)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        ViewBag.Checklist = await _db.TrainingChecklists.AsNoTracking()
            .Where(c => c.EnrollmentId == id).OrderBy(c => c.Step).ToListAsync();
        return View(e);
    }

    public async Task<IActionResult> Complete(Guid id)
    {
        var e = await _db.TrainingEnrollments
            .Include(x => x.Result)
            .Include(x => x.Course)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        if (e.Status is EnrollmentStatus.Completed or EnrollmentStatus.Cancelled)
        {
            TempData["Warning"] = "التسجيل مكتمل أو ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(e);
    }

    public class CompleteVm
    {
        public Guid EnrollmentId { get; set; }
        public decimal Score { get; set; }
        public Guid? CertificateFileId { get; set; }
        public string? EvaluationNotes { get; set; }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(CompleteVm vm)
    {
        var e = await _db.TrainingEnrollments
            .Include(x => x.Result)
            .Include(x => x.Course)
            .FirstOrDefaultAsync(x => x.Id == vm.EnrollmentId);
        if (e == null) return NotFound();
        if (e.Status is EnrollmentStatus.Completed or EnrollmentStatus.Cancelled)
        {
            TempData["Warning"] = "التسجيل مكتمل أو ملغى";
            return RedirectToAction(nameof(Details), new { id = e.Id });
        }

        var checklist = await _db.TrainingChecklists.Where(c => c.EnrollmentId == e.Id).ToListAsync();
        if (!TrainingChecklistDefaults.PreCompleteGate(checklist))
        {
            TempData["Error"] = "أكمل خطوة التسجيل بدليل قبل الإتمام";
            return RedirectToAction(nameof(Details), new { id = e.Id });
        }

        if (vm.CertificateFileId.HasValue)
        {
            try
            {
                await _fileRefs.RegisterReferenceAsync(
                    vm.CertificateFileId.Value,
                    FileReferenceClientExtensions.BuildHolderKey(
                        HrFileServices.HrTraining, nameof(TrainingEnrollment), e.Id, nameof(TrainingEnrollment.CertificateFileId)));
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"مرفق الشهادة مرفوض (FILE-002): {ex.Message}";
                return RedirectToAction(nameof(Complete), new { id = e.Id });
            }
        }

        var completedAt = DateTime.UtcNow;
        e.Status = EnrollmentStatus.Completed;
        e.CertificateNo = LearningCompletionRules.GenerateCertificateNumber(
            e.Course?.Code ?? "CRS", e.EmployeeId, DateOnly.FromDateTime(completedAt));

        if (e.Result == null)
        {
            _db.TrainingResults.Add(new TrainingResult
            {
                EnrollmentId = e.Id,
                Score = vm.Score,
                EvaluationNotes = vm.EvaluationNotes,
                GradedAt = completedAt
            });
        }

        e.CertificateFileId = vm.CertificateFileId;
        e.FinalScore = vm.Score;

        var completedStep = checklist.FirstOrDefault(c => c.Step == TrainingChecklistStep.Completed);
        if (completedStep is not null && !completedStep.IsCompleted)
            TrainingChecklistDefaults.Mark(completedStep, null, $"score:{vm.Score}");
        var certStep = checklist.FirstOrDefault(c => c.Step == TrainingChecklistStep.CertificateIssued);
        if (certStep is not null && !certStep.IsCompleted)
            TrainingChecklistDefaults.Mark(certStep, null, e.CertificateNo!);

        await _db.SaveChangesAsync();

        await _publish.Publish<ITrainingCompleted>(new
        {
            EmployeeId = e.EmployeeId,
            CourseId = e.CourseId,
            Score = vm.Score,
            CertificateFileId = e.CertificateFileId,
            CompletedAt = completedAt,
            CourseTitleAr = e.Course?.TitleAr ?? string.Empty,
            CourseCode = e.Course?.Code ?? string.Empty,
            ProviderId = e.Course?.ProviderId,
            DurationDays = e.Course?.DurationDays ?? 0,
            CorrelationId = Guid.NewGuid(),
            SourceSystem = "HR.Training",
            SchemaVersion = 3
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"تم تسجيل إتمام التدريب — رقم الشهادة {e.CertificateNo}";
        return RedirectToAction(nameof(Details), new { id = e.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id, string? reason)
    {
        var e = await _db.TrainingEnrollments.FindAsync(id);
        if (e == null) return NotFound();
        if (e.Status is EnrollmentStatus.Completed or EnrollmentStatus.Cancelled)
        {
            TempData["Error"] = "لا يمكن إلغاء تسجيل مكتمل أو ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }
        e.Status = EnrollmentStatus.Cancelled;
        e.CancelledAt = DateTime.UtcNow;
        e.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء التسجيل" : reason.Trim();
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء التسجيل (soft-cancel)";
        return RedirectToAction(nameof(Index));
    }
}
