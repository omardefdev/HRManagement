using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;

namespace Records.Archive.Infrastructure.Data;

/// <summary>Populates demo archive classifications, items, files, loans, and custody events.</summary>
public static class DemoArchiveSeeder
{
    private static Guid TenantId => AcceptanceSeedExecution.TenantId;
    private static string ScopedCode(string code) => AcceptanceSeedExecution.CurrentAgency is { } agency
        ? $"{agency.Code}-{code}"
        : code;

    private static readonly (string Code, string NameAr, int Years)[] Classifications =
    [
        ("ADM", "وثائق إدارية", 10),
        ("FIN", "وثائق مالية", 15),
        ("LEG", "وثائق قانونية", 25),
        ("HR", "ملفات موارد بشرية", 50),
        ("CON", "عقود واتفاقيات", 20),
        ("COR", "مراسلات رسمية", 7),
        ("TEC", "وثائق فنية", 12),
        ("AUD", "تقارير تدقيق", 15),
        ("SEC", "وثائق أمنية", 30),
        ("PRJ", "ملفات مشاريع", 10),
        ("MED", "سجلات طبية", 25),
        ("TRN", "سجلات تدريب", 8),
        ("PRO", "محاضر اجتماعات", 5),
        ("ARC", "أرشيف تاريخي", 99),
        ("TMP", "وثائق مؤقتة", 3),
    ];

    private static readonly string[] StorageLocations =
    [
        "مستودع أ — رف 1", "مستودع أ — رف 2", "مستودع ب — رف 1",
        "غرفة الأرشيف المركزي", "خزنة الوثائق السرية", "مستودع فرعي — طابق 2",
    ];

    private static readonly string[] CustodyActions =
    [
        "Created", "CheckedIn", "CheckedOut", "Transferred", "Inspected", "Returned",
    ];

    public static async Task SeedAsync(
        ArchiveDbContext db,
        CancellationToken ct = default)
    {
        if (await db.DocumentClassifications.AnyAsync(ct))
            return;

        var rng = AcceptanceSeedExecution.CreateRandom("records-archive");
        var now = AcceptanceSeedExecution.UtcNow;

        for (var i = 0; i < Classifications.Length; i++)
        {
            var (code, nameAr, years) = Classifications[i];
            db.DocumentClassifications.Add(new DocumentClassification
            {
                Id = DemoSeedIds.DocumentClassificationId(i),
                TenantId = TenantId,
                Code = ScopedCode(code),
                NameAr = nameAr,
                NameEn = code,
                RetentionYears = years,
                RetentionCode = ScopedCode($"ret-{code.ToLowerInvariant()}"),
                CreatedAt = now.AddDays(-365),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);

        var itemCount = AcceptanceSeedExecution.IsAcceptance ? DemoSeedIds.EmployeeCount : 300;
        var itemIds = new Guid[itemCount];
        for (var i = 0; i < itemCount; i++)
        {
            itemIds[i] = DemoSeedIds.Named($"archive-item-{i}");
            var daysAgo = rng.Next(30, 900);
            var format = i % 2 == 0 ? ArchiveFormat.Physical : ArchiveFormat.Digital;
            var classIdx = i % Classifications.Length;

            db.ArchiveItems.Add(new ArchiveItem
            {
                Id = itemIds[i],
                TenantId = TenantId,
                Title = $"وثيقة أرشيفية رقم {i + 1:D4}",
                ReferenceNumber = ScopedCode($"ARC-2026-{i + 1:D5}"),
                ClassificationId = DemoSeedIds.DocumentClassificationId(classIdx),
                StorageLocation = StorageLocations[i % StorageLocations.Length],
                Format = format,
                Creator = DemoSeedIds.EmployeeId(i % DemoSeedIds.EmployeeCount),
                RetentionPeriodYears = Classifications[classIdx].Years,
                IsOnLegalHold = i % 40 == 0,
                LegalHoldAt = i % 40 == 0 ? now.AddDays(-daysAgo / 2) : null,
                LegalHoldReason = i % 40 == 0 ? "حفظ قانوني — قضية قائمة" : null,
                CreatedAt = now.AddDays(-daysAgo),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);

        var physicalCount = AcceptanceSeedExecution.IsAcceptance ? Math.Max(1, itemCount * 2 / 5) : 200;
        for (var i = 0; i < physicalCount; i++)
        {
            var itemIdx = i % itemCount;
            db.PhysicalFileRecords.Add(new PhysicalFileRecord
            {
                TenantId = TenantId,
                ArchiveItemId = itemIds[itemIdx],
                BoxNumber = $"BOX-{(i / 10) + 1:D3}",
                PageCount = rng.Next(5, 250),
                Condition = i % 7 == 0 ? "تحتاج ترميم" : "جيدة",
                CreatedAt = now.AddDays(-rng.Next(10, 800)),
                UpdatedAt = now,
            });
        }

        var digitalCount = AcceptanceSeedExecution.IsAcceptance ? Math.Max(1, itemCount * 13 / 20) : 250;
        for (var i = 0; i < digitalCount; i++)
        {
            var itemIdx = (i + 50) % itemCount;
            db.DigitalFileRecords.Add(new DigitalFileRecord
            {
                TenantId = TenantId,
                ArchiveItemId = itemIds[itemIdx],
                FileId = DemoSeedIds.Named($"archive-file-{i}"),
                FileSizeBytes = rng.Next(50_000, 5_000_000),
                Checksum = Convert.ToHexString(BitConverter.GetBytes(rng.NextInt64())),
                CreatedAt = now.AddDays(-rng.Next(10, 800)),
                UpdatedAt = now,
            });
        }

        // ~200 items get 2 index entries, ~100 get 1 → 500 total
        var indexTarget = AcceptanceSeedExecution.IsAcceptance ? itemCount + itemCount / 2 : 500;
        var indexCount = 0;
        for (var i = 0; i < itemCount && indexCount < indexTarget; i++)
        {
            var entriesForItem = i < itemCount / 2 ? 2 : 1;
            for (var e = 0; e < entriesForItem && indexCount < indexTarget; e++)
            {
                db.ArchiveIndexEntries.Add(new ArchiveIndexEntry
                {
                    TenantId = TenantId,
                    ArchiveItemId = itemIds[i],
                    Keywords = $"أرشيف، وثيقة، {Classifications[i % Classifications.Length].NameAr}",
                    Summary = $"ملخص فهرسة للوثيقة رقم {i + 1} — إدخال {e + 1}",
                    CreatedAt = now.AddDays(-rng.Next(5, 700)),
                    UpdatedAt = now,
                });
                indexCount++;
            }
        }

        for (var i = 0; i < 30; i++)
        {
            var daysAgo = rng.Next(5, 120);
            var loanDate = now.AddDays(-daysAgo);
            var dueDate = loanDate.AddDays(14);
            var status = i switch
            {
                < 15 => LoanStatus.Returned,
                < 25 => LoanStatus.OnLoan,
                _ => LoanStatus.Overdue,
            };
            var person = DemoNameBank.BuildPerson(2000 + i);

            db.LoanRecords.Add(new LoanRecord
            {
                TenantId = TenantId,
                ArchiveItemId = itemIds[i * 7 % itemCount],
                Borrower = DemoNameBank.FullName(person.First, person.Middle, person.Last),
                LoanDate = loanDate,
                DueDate = dueDate,
                ReturnedDate = status == LoanStatus.Returned ? loanDate.AddDays(rng.Next(3, 14)) : null,
                Status = status,
                CreatedAt = loanDate,
                UpdatedAt = now,
            });
        }

        for (var i = 0; i < 50; i++)
        {
            var occurred = now.AddDays(-rng.Next(1, 400));
            db.CustodyEvents.Add(new CustodyEvent
            {
                TenantId = TenantId,
                ArchiveItemId = itemIds[i * 5 % itemCount],
                Action = CustodyActions[i % CustodyActions.Length],
                EvidenceRef = i % 3 == 0 ? $"EVD-ARC-{i + 1:D4}" : null,
                ActorUserId = DemoSeedIds.EmployeeId(i % 40),
                OccurredAt = occurred,
                CreatedAt = occurred,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
