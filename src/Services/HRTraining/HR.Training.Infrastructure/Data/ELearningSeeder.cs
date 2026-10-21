using HR.Training.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using Microsoft.EntityFrameworkCore;

namespace HR.Training.Infrastructure.Data;

public static class ELearningSeeder
{
    private static string ScopedCode(string code) => AcceptanceSeedExecution.CurrentAgency is { } agency
        ? $"{agency.Code}-{code}"
        : code;

    public static async Task SeedAsync(TrainingDbContext db, CancellationToken ct = default)
    {
        if (await db.ELearningCourses.AnyAsync(ct))
            return;

        var governance = new ELearningCourse
        {
            Id = DemoSeedIds.Named("elearning-governance"),
            TenantId = AcceptanceSeedExecution.TenantId,
            Code = ScopedCode("GOV-101"),
            TitleAr = "أساسيات الحوكمة والنزاهة",
            Description = "دورة تعريفية بمبادئ الحوكمة والشفافية والمساءلة في القطاع العام.",
            Category = "الحوكمة",
            EstimatedHours = 6,
            IsPublished = true,
            Modules = new List<CourseModule>
            {
                new()
                {
                    Order = 1,
                    Title = "مدخل إلى الحوكمة",
                    Lessons = new List<Lesson>
                    {
                        new() { Order = 1, Title = "مفهوم الحوكمة وأهدافها" },
                        new() { Order = 2, Title = "مبادئ الشفافية والمساءلة" }
                    }
                },
                new()
                {
                    Order = 2,
                    Title = "تطبيقات عملية",
                    Lessons = new List<Lesson>
                    {
                        new() { Order = 1, Title = "دراسات حالة في النزاهة المؤسسية" }
                    }
                }
            }
        };

        var dataProtection = new ELearningCourse
        {
            Id = DemoSeedIds.Named("elearning-data-protection"),
            TenantId = AcceptanceSeedExecution.TenantId,
            Code = ScopedCode("DP-201"),
            TitleAr = "حماية البيانات الشخصية",
            Description = "التعريف بقانون حماية البيانات الشخصية والتزامات معالجي البيانات.",
            Category = "الامتثال",
            EstimatedHours = 4,
            IsPublished = true,
            Modules = new List<CourseModule>
            {
                new()
                {
                    Order = 1,
                    Title = "الإطار القانوني",
                    Lessons = new List<Lesson>
                    {
                        new() { Order = 1, Title = "حقوق صاحب البيانات" },
                        new() { Order = 2, Title = "ضوابط الموافقة والمعالجة" }
                    }
                }
            }
        };

        StampChildren(governance);
        StampChildren(dataProtection);

        db.ELearningCourses.AddRange(governance, dataProtection);
        await db.SaveChangesAsync(ct);
    }

    private static void StampChildren(ELearningCourse course)
    {
        var tenantId = AcceptanceSeedExecution.TenantId;
        foreach (var module in course.Modules)
        {
            module.Id = DemoSeedIds.Named($"elearning-module-{course.Code}-{module.Order}");
            module.TenantId = tenantId;
            module.CreatedAt = AcceptanceSeedExecution.UtcNow;
            module.UpdatedAt = AcceptanceSeedExecution.UtcNow;

            foreach (var lesson in module.Lessons)
            {
                lesson.Id = DemoSeedIds.Named($"elearning-lesson-{course.Code}-{module.Order}-{lesson.Order}");
                lesson.TenantId = tenantId;
                lesson.CreatedAt = AcceptanceSeedExecution.UtcNow;
                lesson.UpdatedAt = AcceptanceSeedExecution.UtcNow;
            }
        }
    }
}
