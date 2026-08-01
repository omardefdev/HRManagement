using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Infrastructure.Data;

/// <summary>Populates demo vendors, assets, assignments, supply orders, inventory, and maintenance.</summary>
public static class DemoLogisticsSeeder
{
    private static readonly Guid TenantId = TenantDefaults.DefaultTenantId;

    private static readonly string[] VendorNames =
    [
        "شركة النور للتجهيزات المكتبية", "مؤسسة الشام للحواسيب", "شركة الفرات للأثاث",
        "مورد الشرق للأجهزة", "شركة بردى للخدمات اللوجستية", "مؤسسة الجبل للإمداد",
        "شركة اليرموك للقرطاسية", "مورد الساحل التقني", "شركة دمشق للمعدات",
        "مؤسسة حلب للتوريدات", "شركة حمص للخدمات", "مورد اللاذقية الصناعية",
        "شركة طرطوس للمستلزمات", "مؤسسة السويداء التجارية", "شركة درعا للإمداد",
        "مورد إدلب العام", "شركة دير الزور للتجهيز", "مؤسسة الرقة اللوجستية",
        "شركة الحسكة للتوريدات", "مورد القنيطرة المركزي",
    ];

    private static readonly string[] Categories =
    [
        "حواسيب", "أثاث مكتبي", "مركبات", "أجهزة طباعة", "معدات شبكات",
        "أجهزة عرض", "هواتف", "مستلزمات مكتبية", "أجهزة تكييف", "أخرى",
    ];

    private static readonly string[] Brands =
    [
        "Dell", "HP", "Lenovo", "Canon", "Epson", "Samsung", "Cisco", "محلي",
    ];

    private static readonly string[] AssetNames =
    [
        "حاسوب مكتبي", "حاسوب محمول", "طابعة ليزر", "شاشة عرض", "مكتب إداري",
        "كرسي مكتبي", "هاتف IP", "جهاز راوتر", "مكيف هواء", "خزنة وثائق",
        "ماسح ضوئي", "جهاز عرض بروجيكتور", "خادم تخزين", "كاميرا مراقبة", "مركبة خدمة",
    ];

    private static readonly string[] InventoryNames =
    [
        "ورق A4", "أقلام حبر", "حبر طابعة أسود", "حبر طابعة ملون", "ملفات كرتونية",
        "دباسات", "أقراص تخزين", "كبلات شبكة", "فأرة حاسوب", "لوحة مفاتيح",
        "منظف شاشات", "بطاريات AA", "مغلفات رسمية", "أظرف كبيرة", "شريط لاصق",
    ];

    private static readonly string[] Units = ["قطعة", "علبة", "رزمة", "متر", "لتر"];

    public static async Task SeedAsync(
        AdminLogisticsDbContext db,
        CancellationToken ct = default)
    {
        if (await db.Vendors.AnyAsync(ct))
            return;

        var rng = new Random(42);
        var now = DateTime.UtcNow;

        for (var i = 0; i < 20; i++)
        {
            db.Vendors.Add(new Vendor
            {
                Id = DemoSeedIds.VendorId(i),
                TenantId = TenantId,
                Name = VendorNames[i],
                ContactInfo = DemoNameBank.Phone(5000 + i),
                Country = "سورية",
                CreatedAt = now.AddDays(-rng.Next(60, 400)),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);

        var assetStatuses = Enum.GetValues<AssetStatus>();
        for (var i = 0; i < DemoSeedIds.AssetCount; i++)
        {
            var daysAgo = rng.Next(30, 1200);
            var status = assetStatuses[i % assetStatuses.Length];
            // Keep most assignable
            if (i < 180 && status is AssetStatus.Disposed or AssetStatus.Lost)
                status = AssetStatus.InUse;

            db.Assets.Add(new Asset
            {
                Id = DemoSeedIds.AssetId(i),
                TenantId = TenantId,
                TagNumber = $"AST-{i + 1:D5}",
                Name = $"{AssetNames[i % AssetNames.Length]} {i + 1}",
                Category = Categories[i % Categories.Length],
                Brand = Brands[i % Brands.Length],
                Model = $"M{(i % 20) + 1}",
                PurchaseDate = now.AddDays(-daysAgo),
                Value = rng.Next(50_000, 5_000_000),
                Status = status,
                IsPrivileged = i % 25 == 0,
                CreatedAt = now.AddDays(-daysAgo),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);

        // 300 assignments: assets 0–99 get returned + open; assets 100–199 get one open.
        for (var i = 0; i < 100; i++)
        {
            var assigned = now.AddDays(-rng.Next(90, 400));
            db.AssetAssignments.Add(new AssetAssignment
            {
                TenantId = TenantId,
                AssetId = DemoSeedIds.AssetId(i),
                EmployeeId = DemoSeedIds.EmployeeId(i % DemoSeedIds.EmployeeCount),
                AssignedAt = assigned,
                DueBackDate = assigned.AddDays(180),
                ConditionOnAssign = "جيدة",
                ReturnedAt = assigned.AddDays(rng.Next(30, 120)),
                EvidenceRef = i % 25 == 0 ? $"EVD-AST-{i + 1:D4}" : null,
                CreatedAt = assigned,
                UpdatedAt = now,
            });
        }

        for (var i = 0; i < 200; i++)
        {
            var assigned = now.AddDays(-rng.Next(5, 80));
            var assetIdx = i; // 0..199
            db.AssetAssignments.Add(new AssetAssignment
            {
                TenantId = TenantId,
                AssetId = DemoSeedIds.AssetId(assetIdx),
                EmployeeId = DemoSeedIds.EmployeeId((i * 3 + 7) % DemoSeedIds.EmployeeCount),
                AssignedAt = assigned,
                DueBackDate = assigned.AddDays(365),
                ConditionOnAssign = "جيدة",
                ReturnedAt = null,
                EvidenceRef = assetIdx % 25 == 0 ? $"EVD-AST-OPEN-{assetIdx + 1:D4}" : null,
                CreatedAt = assigned,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);

        var orderStatuses = Enum.GetValues<SupplyOrderStatus>();
        var orderIds = new Guid[50];
        for (var i = 0; i < 50; i++)
        {
            orderIds[i] = DemoSeedIds.Named($"supply-order-{i}");
            var daysAgo = rng.Next(3, 180);
            var status = orderStatuses[i % orderStatuses.Length];
            var orderedAt = now.AddDays(-daysAgo);

            db.SupplyOrders.Add(new SupplyOrder
            {
                Id = orderIds[i],
                TenantId = TenantId,
                RequestedBy = DemoSeedIds.EmployeeId(i % DemoSeedIds.EmployeeCount),
                OrgUnitId = DemoSeedIds.OrgUnitId(1 + (i % 8)),
                Items = $"طلب تجهيز رقم {i + 1}: {InventoryNames[i % InventoryNames.Length]} × {rng.Next(5, 50)}",
                Status = status,
                OrderedAt = orderedAt,
                FulfilledAt = status == SupplyOrderStatus.Fulfilled ? orderedAt.AddDays(rng.Next(2, 14)) : null,
                CreatedAt = orderedAt,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);

        for (var i = 0; i < 50; i++)
        {
            var received = now.AddDays(-rng.Next(1, 160));
            db.ReceiptRecords.Add(new ReceiptRecord
            {
                TenantId = TenantId,
                SupplyOrderId = orderIds[i],
                ReceivedBy = DemoSeedIds.EmployeeId((i + 10) % DemoSeedIds.EmployeeCount),
                ReceivedAt = received,
                ItemsReceived = $"استلام طلب {i + 1}: {InventoryNames[i % InventoryNames.Length]}",
                CreatedAt = received,
                UpdatedAt = now,
            });
        }

        for (var i = 0; i < 100; i++)
        {
            db.InventoryItems.Add(new InventoryItem
            {
                TenantId = TenantId,
                Sku = $"SKU-{i + 1:D4}",
                Name = $"{InventoryNames[i % InventoryNames.Length]} — {i + 1}",
                Quantity = rng.Next(0, 500),
                Unit = Units[i % Units.Length],
                Location = StorageShelf(i),
                ReorderThreshold = rng.Next(10, 50),
                CreatedAt = now.AddDays(-rng.Next(30, 500)),
                UpdatedAt = now,
            });
        }

        var maintTypes = Enum.GetValues<MaintenanceType>();
        for (var i = 0; i < 40; i++)
        {
            var performed = now.AddDays(-rng.Next(5, 300));
            db.MaintenanceRecords.Add(new MaintenanceRecord
            {
                TenantId = TenantId,
                AssetId = DemoSeedIds.AssetId(i * 4 % DemoSeedIds.AssetCount),
                MaintenanceType = maintTypes[i % maintTypes.Length],
                PerformedAt = performed,
                PerformedBy = DemoNameBank.FullName(
                    DemoNameBank.BuildPerson(3000 + i).First,
                    null,
                    DemoNameBank.BuildPerson(3000 + i).Last),
                Cost = rng.Next(10_000, 400_000),
                Notes = maintTypes[i % maintTypes.Length] == MaintenanceType.Preventive
                    ? "صيانة دورية وقائية"
                    : "إصلاح عطل فني",
                CreatedAt = performed,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static string StorageShelf(int i) =>
        $"مستودع {(char)('A' + i % 4)} — رف {(i % 12) + 1}";
}
