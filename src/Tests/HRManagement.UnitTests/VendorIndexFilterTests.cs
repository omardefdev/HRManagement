using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class VendorIndexFilterTests
{
    private static async Task<(AdminLogisticsDbContext Db, Guid WithContactId, Guid MissingContactId)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AdminLogisticsDbContext(options);

        var damascus = new Vendor
        {
            Id = Guid.NewGuid(),
            Name = "شركة النور للتجهيزات",
            ContactInfo = "info@alnoor.sy",
            Country = "سوريا"
        };
        var aleppo = new Vendor
        {
            Id = Guid.NewGuid(),
            Name = "مؤسسة حلب للتوريدات",
            ContactInfo = "+963-21-1234567",
            Country = "سوريا"
        };
        var jordan = new Vendor
        {
            Id = Guid.NewGuid(),
            Name = "Jordan Supplies Co",
            ContactInfo = "sales@jo-supplies.jo",
            Country = "الأردن"
        };
        var noContact = new Vendor
        {
            Id = Guid.NewGuid(),
            Name = "مورد بدون اتصال",
            Country = "سوريا"
        };
        var emptyContact = new Vendor
        {
            Id = Guid.NewGuid(),
            Name = "مورد اتصال فارغ",
            ContactInfo = "",
            Country = "لبنان"
        };

        db.Vendors.AddRange(damascus, aleppo, jordan, noContact, emptyContact);
        await db.SaveChangesAsync();

        return (db, damascus.Id, noContact.Id);
    }

    private static async Task<List<Vendor>> QueryAsync(AdminLogisticsDbContext db, VendorIndexFilter filter)
    {
        var query = filter.Apply(db.Vendors.AsNoTracking());
        return await query.OrderBy(v => v.Name).ToListAsync();
    }

    [Fact]
    public async Task Filter_by_q_matches_name()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new VendorIndexFilter { Q = "النور" });
            Assert.Single(items);
            Assert.Contains("النور", items[0].Name);
        }
    }

    [Fact]
    public async Task Filter_by_q_matches_country()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new VendorIndexFilter { Q = "الأردن" });
            Assert.Single(items);
            Assert.Equal("Jordan Supplies Co", items[0].Name);
        }
    }

    [Fact]
    public async Task Filter_by_q_matches_contact()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new VendorIndexFilter { Q = "alnoor" });
            Assert.Single(items);
            Assert.Equal("info@alnoor.sy", items[0].ContactInfo);
        }
    }

    [Fact]
    public async Task Filter_by_country()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new VendorIndexFilter { Country = "سوريا" });
            Assert.Equal(3, items.Count);
            Assert.All(items, v => Assert.Equal("سوريا", v.Country));
        }
    }

    [Fact]
    public async Task Filter_hasContactOnly()
    {
        var (db, withContactId, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new VendorIndexFilter { HasContactOnly = true });
            Assert.Equal(3, items.Count);
            Assert.Contains(items, v => v.Id == withContactId);
            Assert.All(items, v => Assert.False(string.IsNullOrEmpty(v.ContactInfo)));
        }
    }

    [Fact]
    public async Task Filter_missingContactOnly()
    {
        var (db, _, missingContactId) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new VendorIndexFilter { MissingContactOnly = true });
            Assert.Equal(2, items.Count);
            Assert.Contains(items, v => v.Id == missingContactId);
            Assert.All(items, v => Assert.True(string.IsNullOrEmpty(v.ContactInfo)));
        }
    }

    [Fact]
    public async Task Filter_combined_country_and_hasContactOnly()
    {
        var (db, _, _) = await SeedAsync();
        await using (db)
        {
            var items = await QueryAsync(db, new VendorIndexFilter
            {
                Country = "سوريا",
                HasContactOnly = true
            });
            Assert.Equal(2, items.Count);
            Assert.All(items, v =>
            {
                Assert.Equal("سوريا", v.Country);
                Assert.False(string.IsNullOrEmpty(v.ContactInfo));
            });
        }
    }
}
