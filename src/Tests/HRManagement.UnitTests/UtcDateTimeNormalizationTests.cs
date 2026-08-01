using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class UtcDateTimeNormalizationTests
{
    [Fact]
    public void AsUtc_converts_unspecified_date_only_values()
    {
        var date = new DateTime(2020, 6, 1);
        Assert.Equal(DateTimeKind.Utc, date.AsUtc().Kind);
    }

    [Fact]
    public void AsUtc_preserves_existing_utc_values()
    {
        var date = new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(date, date.AsUtc());
    }

    [Fact]
    public void Apply_converts_unspecified_datetime_properties_to_utc()
    {
        var options = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new ContractsDbContext(options);
        var signature = new ContractSignature
        {
            ContractId = Guid.NewGuid(),
            SignedBy = Guid.NewGuid(),
            SignedAt = new DateTime(2025, 1, 15, 10, 30, 0),
            SignatureType = SignatureType.Digital
        };

        db.ContractSignatures.Add(signature);
        UtcDateTimeNormalization.Apply(db);

        Assert.Equal(DateTimeKind.Utc, signature.SignedAt.Kind);
    }
}
