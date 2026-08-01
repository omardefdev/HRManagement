using HR.Training.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Training.Infrastructure.Data;

public static class ELearningSeeder
{
    public static async Task SeedAsync(TrainingDbContext db, CancellationToken ct = default)
    {
        if (await db.ELearningCourses.AnyAsync(ct))
            return;

        var governance = new ELearningCourse
        {
            Code = "GOV-101",
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
            Code = "DP-201",
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

        db.ELearningCourses.AddRange(governance, dataProtection);
        await db.SaveChangesAsync(ct);
    }
}
