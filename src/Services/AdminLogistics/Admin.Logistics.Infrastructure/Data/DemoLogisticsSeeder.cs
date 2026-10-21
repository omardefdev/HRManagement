using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Infrastructure.Data;

/// <summary>Populates demo vendors, assets, assignments, supply orders, inventory, and maintenance.</summary>
public static class DemoLogisticsSeeder
{
    private static Guid TenantId => AcceptanceSeedExecution.TenantId;

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

        var rng = AcceptanceSeedExecution.CreateRandom("admin-logistics");
        var now = AcceptanceSeedExecution.UtcNow;

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

        var inventoryIds = new Guid[100];
        for (var i = 0; i < 100; i++)
        {
            inventoryIds[i] = DemoSeedIds.Named($"inventory-item-{i}");
            db.InventoryItems.Add(new InventoryItem
            {
                Id = inventoryIds[i],
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

        await db.SaveChangesAsync(ct);

        var orderStatuses = Enum.GetValues<SupplyOrderStatus>();
        var orderIds = new Guid[50];
        var receiptIds = new Guid[50];
        var createdReceiptIds = new List<Guid>();
        for (var i = 0; i < 50; i++)
        {
            orderIds[i] = DemoSeedIds.Named($"supply-order-{i}");
            receiptIds[i] = DemoSeedIds.Named($"receipt-{i}");
            var daysAgo = rng.Next(3, 180);
            var status = orderStatuses[i % orderStatuses.Length];
            var orderedAt = now.AddDays(-daysAgo);
            var isFulfilled = status == SupplyOrderStatus.Fulfilled;

            db.SupplyOrders.Add(new SupplyOrder
            {
                Id = orderIds[i],
                TenantId = TenantId,
                RequestedBy = DemoSeedIds.EmployeeId(i % DemoSeedIds.EmployeeCount),
                OrgUnitId = DemoSeedIds.EmployeeOrgUnitId(i % DemoSeedIds.EmployeeCount),
                Items = $"طلب تجهيز رقم {i + 1}: {InventoryNames[i % InventoryNames.Length]} × {rng.Next(5, 50)}",
                Status = status,
                OrderedAt = orderedAt,
                FulfilledAt = isFulfilled ? orderedAt.AddDays(rng.Next(2, 14)) : null,
                CreatedAt = orderedAt,
                UpdatedAt = now,
            });

            if (isFulfilled)
            {
                var lineCount = rng.Next(1, 4);
                for (var j = 0; j < lineCount; j++)
                {
                    var invIdx = (i * 3 + j) % 100;
                    var qty = rng.Next(1, 8);
                    var isCap = (i + j) % 3 == 0;
                    db.SupplyOrderLines.Add(new SupplyOrderLine
                    {
                        TenantId = TenantId,
                        SupplyOrderId = orderIds[i],
                        Sku = $"SKU-{invIdx + 1:D4}",
                        QtyRequested = qty,
                        QtyReceived = qty,
                        UnitCost = rng.Next(100_000, 2_000_000),
                        IsCapitalizable = isCap,
                        InventoryItemId = inventoryIds[invIdx],
                        CreatedAt = orderedAt,
                        UpdatedAt = now,
                    });
                }

                var received = now.AddDays(-rng.Next(1, 160));
                db.ReceiptRecords.Add(new ReceiptRecord
                {
                    Id = receiptIds[i],
                    TenantId = TenantId,
                    SupplyOrderId = orderIds[i],
                    ReceivedBy = DemoSeedIds.EmployeeId((i + 10) % DemoSeedIds.EmployeeCount),
                    ReceivedAt = received,
                    ItemsReceived = $"استلام طلب {i + 1}: {InventoryNames[i % InventoryNames.Length]}",
                    InventoryItemId = inventoryIds[i % 100],
                    QuantityReceived = rng.Next(1, 20),
                    CreatedAt = received,
                    UpdatedAt = now,
                });
                createdReceiptIds.Add(receiptIds[i]);
            }
            else if (status != SupplyOrderStatus.Cancelled)
            {
                db.ReceiptRecords.Add(new ReceiptRecord
                {
                    Id = receiptIds[i],
                    TenantId = TenantId,
                    SupplyOrderId = orderIds[i],
                    ReceivedBy = DemoSeedIds.EmployeeId((i + 10) % DemoSeedIds.EmployeeCount),
                    ReceivedAt = now.AddDays(-rng.Next(1, 160)),
                    ItemsReceived = $"استلام طلب {i + 1}: {InventoryNames[i % InventoryNames.Length]}",
                    CreatedAt = now.AddDays(-rng.Next(1, 160)),
                    UpdatedAt = now,
                });
                createdReceiptIds.Add(receiptIds[i]);
            }
        }

        await db.SaveChangesAsync(ct);

        var reservedAcceptanceAssets = AcceptanceSeedExecution.CurrentAgency?.Id == AcceptanceSeedCatalog.AgencyAId
            ? 1
            : 0;
        var assetCount = DemoSeedIds.AssetCount - reservedAcceptanceAssets;
        if (assetCount <= 0)
            throw new InvalidOperationException("The acceptance logistics volume must leave room for the privileged fixture.");

        var assetStatuses = Enum.GetValues<AssetStatus>();
        for (var i = 0; i < assetCount; i++)
        {
            var daysAgo = rng.Next(30, 1200);
            var status = assetStatuses[i % assetStatuses.Length];
            if (i < 180 && status is AssetStatus.Disposed or AssetStatus.Lost)
                status = AssetStatus.InUse;

            Guid? originReceiptId = null;
            Guid? originLineId = null;
            var isLegacy = i is >= 120 and < 140;
            string? legacyReason = isLegacy ? "هبة / موجود قبل تفعيل سلسلة التوريد" : null;

            if (!isLegacy && i < 72 && createdReceiptIds.Count > 0)
                originReceiptId = createdReceiptIds[i % createdReceiptIds.Count];

            db.Assets.Add(new Asset
            {
                Id = DemoSeedIds.AssetId(i),
                TenantId = TenantId,
                TagNumber = $"{(AcceptanceSeedExecution.CurrentAgency?.Code ?? "DEMO")}-{i + 1:D5}",
                Name = $"{AssetNames[i % AssetNames.Length]} {i + 1}",
                Category = Categories[i % Categories.Length],
                Brand = Brands[i % Brands.Length],
                Model = $"M{(i % 20) + 1}",
                PurchaseDate = now.AddDays(-daysAgo),
                Value = rng.Next(50_000, 5_000_000),
                Status = status,
                IsPrivileged = false,
                OriginReceiptRecordId = originReceiptId,
                OriginSupplyOrderLineId = originLineId,
                IsLegacyEntry = isLegacy,
                LegacyEntryReason = legacyReason,
                CreatedAt = now.AddDays(-daysAgo),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);

        var assignmentCount = AcceptanceSeedExecution.AssetAssignmentCount - reservedAcceptanceAssets;
        var returnedCount = assignmentCount / 3;
        var activeCount = assignmentCount - returnedCount;

        for (var i = 0; i < returnedCount; i++)
        {
            var assigned = now.AddDays(-rng.Next(90, 400));
            db.AssetAssignments.Add(new AssetAssignment
            {
                TenantId = TenantId,
                AssetId = DemoSeedIds.AssetId(i),
                EmployeeId = DemoSeedIds.EmployeeId(i % DemoSeedIds.EmployeeCount),
                AssignedAt = assigned,
                DueBackDate = assigned.AddDays(180),
                HandoverCondition = AssetHandoverCondition.Good,
                ReturnedAt = assigned.AddDays(rng.Next(30, 120)),
                EvidenceRef = i % 25 == 0 ? $"EVD-AST-{i + 1:D4}" : null,
                CreatedAt = assigned,
                UpdatedAt = now,
            });
        }

        for (var i = 0; i < activeCount; i++)
        {
            var assigned = now.AddDays(-rng.Next(5, 80));
            var assetIdx = i % assetCount;
            db.AssetAssignments.Add(new AssetAssignment
            {
                TenantId = TenantId,
                AssetId = DemoSeedIds.AssetId(assetIdx),
                EmployeeId = DemoSeedIds.EmployeeId((i * 3 + 7) % DemoSeedIds.EmployeeCount),
                AssignedAt = assigned,
                DueBackDate = assigned.AddDays(365),
                HandoverCondition = AssetHandoverCondition.Good,
                ReturnedAt = null,
                EvidenceRef = assetIdx % 25 == 0 ? $"EVD-AST-OPEN-{assetIdx + 1:D4}" : null,
                CreatedAt = assigned,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);

        var maintTypes = Enum.GetValues<MaintenanceType>();
        for (var i = 0; i < 40; i++)
        {
            var performed = now.AddDays(-rng.Next(5, 300));
            db.MaintenanceRecords.Add(new MaintenanceRecord
            {
                TenantId = TenantId,
                AssetId = DemoSeedIds.AssetId(i * 4 % assetCount),
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
