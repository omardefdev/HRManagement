using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Localization;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>HCM-029: hire-closure fixtures (SQLite context from <see cref="Hcm027TestSupport"/>).</summary>
internal static class Hcm029TestSupport
{
    public static readonly Guid HrUserId = Guid.Parse("29290000-0000-4000-8000-000000000001");

    public const string ValidReason = "تصحيح خطأ إدخال في طلب التوظيف بعد مطابقة الهوية";

    public static IConfiguration Config(params (string Key, string? Value)[] extra)
    {
        var values = new Dictionary<string, string?>
        {
            ["Recruitment:RejectSiblingApplicationsOnHire"] = "true",
            ["Recruitment:LegacyHireWithoutOffer"] = "false",
            ["PolicyEngine:DefaultMode"] = "Off",
            ["PolicyEngine:Core.Recruitment"] = "Off"
        };
        foreach (var (key, value) in extra)
            values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>Publish mock that accepts every call; inspect <see cref="Published"/> afterwards.</summary>
    public static Mock<IPublishEndpoint> CreatePublishMock() => new();

    /// <summary>(message type, anonymous initializer) for each <c>Publish&lt;T&gt;(object values, ct)</c> call.</summary>
    public static IReadOnlyList<(Type Type, object Values)> Published(Mock<IPublishEndpoint> publish) =>
        publish.Invocations
            .Where(i => i.Method.Name == nameof(IPublishEndpoint.Publish) && i.Method.IsGenericMethod)
            .Select(i => (i.Method.GetGenericArguments()[0], i.Arguments[0]))
            .ToList();

    public static object? Prop(object values, string name) =>
        values.GetType().GetProperty(name)?.GetValue(values);

    public static RecruitmentHireClosureService CreateClosure(
        HRCoreDbContext db,
        IPublishEndpoint? publish = null,
        IConfiguration? config = null)
    {
        var legalValidator = new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db));
        var regimeValidator = new ServiceRegimeValidator(db, legalValidator);
        var exceptionService = new ServiceRegimeExceptionService(
            db, regimeValidator, new ServiceRegimeTransitionService(db, regimeValidator));
        return new RecruitmentHireClosureService(
            db,
            new EstablishmentService(),
            new FundedPositionVersionService(db),
            new RequisitionCloseService(),
            legalValidator,
            regimeValidator,
            exceptionService,
            new ReserveListService(
                db,
                Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()),
                NullLogger<ReserveListService>.Instance),
            publish ?? CreatePublishMock().Object,
            config ?? Config(),
            NullLogger<RecruitmentHireClosureService>.Instance);
    }

    /// <summary>
    /// Employee as the PII interceptors would store it: the column holds an opaque (encrypted) value, the hash and the
    /// normalized name are set. The unit-test context has no interceptors, so they are set here.
    /// </summary>
    public static async Task<Employee> SeedEmployeeAsync(
        HRCoreDbContext db,
        string nationalId,
        string fullName = "موظف قائم",
        DateTime? birthDate = null)
    {
        var employee = new Employee
        {
            NationalId = "ENC:" + Guid.NewGuid().ToString("N"),
            NationalIdHash = PiiMask.Sha256Hex(nationalId),
            FirstName = fullName.Split(' ')[0],
            LastName = fullName.Split(' ')[^1],
            FullName = fullName,
            FullNameNormalized = ArabicTextNormalizer.Normalize(fullName),
            BirthDate = birthDate ?? new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    public static RecruitmentApplicationsController CreateHireController(
        HRCoreDbContext db,
        IConfiguration? config = null,
        RecruitmentHireClosureService? closure = null)
    {
        config ??= Config();
        var authorization = new Mock<IAuthorizationService>();
        authorization
            .Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());

        var controller = new RecruitmentApplicationsController(
            db,
            new RecruitmentIntegrationService(closure ?? CreateClosure(db, config: config)),
            new RecruitmentApplicationIntakeService(db),
            new RecruitmentSelectionService(db),
            Hcm027TestSupport.CreateOfferService(db, config),
            null!,
            null!,
            null!,
            authorization.Object,
            new LegislationRegisterProvider(db),
            Mock.Of<IPolicyOrchestrator>(),
            config,
            new HirePersonMatchService(db),
            new UnifiedIntakeService(db));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, HrUserId.ToString()),
                new Claim(ClaimTypes.Role, HrManagementRoles.HrCore)
            ], "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }
}
