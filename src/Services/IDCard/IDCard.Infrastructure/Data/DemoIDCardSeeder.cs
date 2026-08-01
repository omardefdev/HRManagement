using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace IDCard.Infrastructure.Data;

/// <summary>Populates demo card designs, issued cards, requests, deliveries, and access permissions.</summary>
public static class DemoIDCardSeeder
{
    private static readonly Guid TenantId = TenantDefaults.DefaultTenantId;

    private static readonly (string Name, string Template, int ValidityDays)[] Designs =
    [
        ("عادية", "standard", 1095),
        ("VIP", "vip", 1825),
        ("مؤقتة", "temporary", 90),
    ];

    private static readonly string[] Resources =
    [
        "المبنى الرئيسي",
        "مواقف السيارات",
        "مركز البيانات",
        "الأرشيف المركزي",
        "قاعة الاجتماعات",
    ];

    private static readonly string[] AccessLevels = ["قراءة", "دخول", "كامل"];

    public static async Task SeedAsync(
        IDCardDbContext db,
        CancellationToken ct = default)
    {
        TenantIdFlow.Set(TenantId);
        try
        {
            if (await db.CardDesigns.AnyAsync(ct))
                return;

            var rng = new Random(42);
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);

            await SeedDesignsAsync(db, now, ct);
            var cards = await SeedIssuedCardsAsync(db, rng, now, today, ct);
            await SeedDeliveriesAsync(db, cards, rng, now, ct);
            await SeedRequestsAsync(db, cards, rng, now, today, ct);
            await SeedAccessPermissionsAsync(db, cards, rng, now, today, ct);
        }
        finally
        {
            TenantIdFlow.Set(null);
        }
    }

    private static async Task SeedDesignsAsync(IDCardDbContext db, DateTime now, CancellationToken ct)
    {
        for (var i = 0; i < Designs.Length; i++)
        {
            var (name, template, validityDays) = Designs[i];
            db.CardDesigns.Add(new CardDesign
            {
                Id = DemoSeedIds.CardDesignId(i),
                TenantId = TenantId,
                Name = name,
                Template = template,
                ValidityDays = validityDays,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<List<IssuedCard>> SeedIssuedCardsAsync(
        IDCardDbContext db,
        Random rng,
        DateTime now,
        DateOnly today,
        CancellationToken ct)
    {
        const int issuedCount = 450;
        var cards = new List<IssuedCard>(issuedCount);

        for (var i = 0; i < issuedCount; i++)
        {
            var designIndex = DemoSeedIds.IsSupervisor(i)
                ? 1 // VIP
                : i % 17 == 0
                    ? 2 // مؤقتة
                    : 0; // عادية

            var validityDays = Designs[designIndex].ValidityDays;
            var daysAgo = rng.Next(30, 700);
            var issuedAt = now.AddDays(-daysAgo);
            var expires = DateOnly.FromDateTime(issuedAt).AddDays(validityDays);

            var status = expires < today
                ? CardStatus.Expired
                : i % 97 == 0
                    ? CardStatus.Lost
                    : i % 89 == 0
                        ? CardStatus.Revoked
                        : CardStatus.Active;

            var card = new IssuedCard
            {
                Id = DemoSeedIds.Named($"issued-card-{i}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(i),
                CardDesignId = DemoSeedIds.CardDesignId(designIndex),
                IssuedAt = issuedAt,
                ExpiresAt = expires,
                Status = status,
                SerialNumber = $"IDC-{i:D6}",
                RevokedAt = status == CardStatus.Revoked ? issuedAt.AddDays(rng.Next(10, 120)) : null,
                UrgentRevoke = status == CardStatus.Revoked && i % 2 == 0,
                CreatedAt = issuedAt,
                UpdatedAt = now,
            };

            db.IssuedCards.Add(card);
            cards.Add(card);
        }

        await db.SaveChangesAsync(ct);
        return cards;
    }

    private static async Task SeedDeliveriesAsync(
        IDCardDbContext db,
        List<IssuedCard> cards,
        Random rng,
        DateTime now,
        CancellationToken ct)
    {
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var printedAt = card.IssuedAt.AddHours(rng.Next(1, 48));
            var deliveredAt = printedAt.AddHours(rng.Next(1, 72));
            var deliveryId = DemoSeedIds.Named($"card-delivery-{i}");

            db.CardDeliveryRecords.Add(new CardDeliveryRecord
            {
                Id = deliveryId,
                TenantId = TenantId,
                IssuedCardId = card.Id,
                PrintedBy = DemoSeedIds.EmployeeId(rng.Next(0, 40)),
                PrintedAt = printedAt,
                DeliveredTo = card.EmployeeId,
                DeliveredAt = deliveredAt,
                SignatureOrReceipt = $"إيصال استلام البطاقة {card.SerialNumber}",
                CreatedAt = printedAt,
                UpdatedAt = now,
            });

            card.DeliveryRecordId = deliveryId;
            card.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedRequestsAsync(
        IDCardDbContext db,
        List<IssuedCard> cards,
        Random rng,
        DateTime now,
        DateOnly today,
        CancellationToken ct)
    {
        // 450 Processed (linked to issued cards) + 50 Pending
        for (var i = 0; i < 450; i++)
        {
            var card = cards[i];
            var requestDate = DateOnly.FromDateTime(card.IssuedAt).AddDays(-rng.Next(1, 14));
            var purpose = i % 11 == 0
                ? CardRequestPurpose.Replacement
                : i % 19 == 0
                    ? CardRequestPurpose.Update
                    : CardRequestPurpose.New;

            db.CardRequests.Add(new CardRequest
            {
                Id = DemoSeedIds.Named($"card-request-{i}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(i),
                RequestedBy = DemoSeedIds.EmployeeId(i),
                RequestDate = requestDate,
                Purpose = purpose,
                Status = CardRequestStatus.Processed,
                IssuedCardId = card.Id,
                CreatedAt = requestDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                UpdatedAt = now,
            });
        }

        for (var i = 450; i < 500; i++)
        {
            var daysAgo = rng.Next(1, 30);
            var purpose = (CardRequestPurpose)(rng.Next(3));

            db.CardRequests.Add(new CardRequest
            {
                Id = DemoSeedIds.Named($"card-request-{i}"),
                TenantId = TenantId,
                EmployeeId = DemoSeedIds.EmployeeId(i),
                RequestedBy = DemoSeedIds.EmployeeId(i),
                RequestDate = today.AddDays(-daysAgo),
                Purpose = purpose,
                Status = CardRequestStatus.Pending,
                CreatedAt = now.AddDays(-daysAgo),
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAccessPermissionsAsync(
        IDCardDbContext db,
        List<IssuedCard> cards,
        Random rng,
        DateTime now,
        DateOnly today,
        CancellationToken ct)
    {
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var validFrom = DateOnly.FromDateTime(card.IssuedAt);
            var validTo = card.ExpiresAt;

            // Two permissions per issued card (~900 total)
            for (var p = 0; p < 2; p++)
            {
                var resource = Resources[(i + p) % Resources.Length];
                var level = AccessLevels[(i + p) % AccessLevels.Length];

                db.AccessPermissions.Add(new AccessPermission
                {
                    Id = DemoSeedIds.Named($"access-permission-{i}-{p}"),
                    TenantId = TenantId,
                    IssuedCardId = card.Id,
                    Resource = resource,
                    AccessLevel = level,
                    ValidFrom = validFrom,
                    ValidTo = validTo ?? today.AddDays(365),
                    CreatedAt = card.IssuedAt,
                    UpdatedAt = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
