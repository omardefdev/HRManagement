using HR.Contracts.Application.Abstractions;
using HR.Contracts.Application.Features.HireCases;
using HR.Contracts.Application.Features.InboundEvents;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>FUN-029: HR.Contracts fixtures (EF InMemory, like InboundServiceTransactionTests).</summary>
internal static class Fun029TestSupport
{
    public static ContractsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ContractsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    public static async Task<ContractType> SeedContractTypeAsync(ContractsDbContext db, string code, string? nameAr = null)
    {
        var type = new ContractType { Code = code, NameAr = nameAr ?? code };
        db.ContractTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }

    public static ProcessCandidateAcceptedHandler CreateHandler(
        ContractsDbContext db,
        IEventPublisher? publisher = null,
        bool allowDefaultFallback = true,
        HR.Contracts.Domain.Services.HireChecklistOptions? checklist = null) =>
        new(
            new ContractsRepository(db),
            CodeGenerationTestSupport.CreateClient(),
            NullLogger<ProcessCandidateAcceptedHandler>.Instance,
            Microsoft.Extensions.Options.Options.Create(new HireContractOptions { AllowDefaultContractTypeFallback = allowDefaultFallback }),
            publisher ?? Mock.Of<IEventPublisher>(),
            Microsoft.Extensions.Options.Options.Create(checklist ?? new HR.Contracts.Domain.Services.HireChecklistOptions()));

    /// <summary>A v4 command as CandidateAcceptedConsumer builds it for an external hire.</summary>
    public static ProcessCandidateAcceptedCommand V4Command(
        Guid employeeId,
        Guid? applicationId = null,
        string? legalRegimeId = "law50",
        string? employmentType = "Permanent",
        bool internalMobility = false) =>
        new(
            employeeId,
            employmentType ?? "PERMANENT",
            new DateOnly(2026, 12, 1),
            "مرشح الاختبار",
            applicationId ?? Guid.NewGuid(),
            RequisitionId: Guid.Parse("29290000-0000-4000-8000-0000000000a1"),
            OfferLetterId: Guid.Parse("29290000-0000-4000-8000-0000000000b1"),
            OfferedSalary: 5000m,
            GradeCode: "G5",
            LegalRegimeId: legalRegimeId,
            EmploymentType: employmentType,
            IsInternalMobility: internalMobility);
}
