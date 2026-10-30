using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using MassTransit.Testing;
using EmploymentType = HRManagement.SharedKernel.Enums.EmploymentType;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HRManagement.UnitTests.Hcm029TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-029 (C): ICandidateAccepted v4 carries the offer terms; internal mobility does not publish it.</summary>
public class Hcm029CandidateAcceptedV4Tests
{
    private static DateOnly Start => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

    [Fact]
    public void Contract_v4_defaults_keep_v3_implementations_valid()
    {
        ICandidateAccepted v3 = new V3Stub(Guid.NewGuid(), "PERMANENT", DateTime.UtcNow, "مرشح", "HR.Core", null);

        Assert.Equal(4, v3.SchemaVersion);
        Assert.Null(v3.OfferedSalary);
        Assert.Null(v3.GradeCode);
        Assert.Null(v3.LegalRegimeId);
        Assert.Null(v3.PayrollRegimeCode);
        Assert.Null(v3.EmploymentType);
        Assert.Null(v3.PositionCode);
        Assert.Null(v3.OrgUnitId);
        Assert.False(v3.IsInternalMobility);
    }

    [Fact]
    public async Task External_hire_publishes_v4_with_the_offer_terms()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, req) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var position = await db.FundedPositions.AsNoTracking().SingleAsync(p => p.Id == req.FundedPositionId);
        var offer = await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == winner.ActiveOfferLetterId);
        var publish = CreatePublishMock();

        var employee = await CreateClosure(db, publish.Object).HireAndCreateEmployeeAsync(
            winner, winner.NationalId!, "مرشح", null, "خارجي", DateTime.UtcNow.AddYears(-30), Gender.Male, null, Start,
            "PERMANENT");

        var published = Published(publish);
        // HCM-032: the hire filled the last vacancy, so the requisition-closed aggregate (no person data) follows.
        Assert.Equal([typeof(ICandidateAccepted), typeof(IEmployeeCreated), typeof(IRecruitmentRequisitionClosed)], published.Select(p => p.Type));
        var values = published[0].Values;

        // Round trip through MassTransit so a misspelt member fails here, not in HR.Contracts.
        var message = await RoundTripAsync(values);
        Assert.Equal(4, message.SchemaVersion);
        Assert.Equal(employee.Id, message.EmployeeId);
        Assert.Equal(winner.Id, message.RecruitmentApplicationId);
        Assert.Equal(offer.Id, message.OfferLetterId);
        Assert.Equal(5000m, message.OfferedSalary);
        Assert.Equal("G5", message.GradeCode);
        Assert.Equal("law50", message.LegalRegimeId);
        Assert.Equal(ServiceRegimeToPayrollMapper.PayrollLaw50, message.PayrollRegimeCode);
        Assert.Equal(nameof(EmploymentType.Permanent), message.EmploymentType);
        Assert.Equal(nameof(EmploymentType.Permanent), message.EmploymentCategory);
        Assert.Equal(position.PositionCode, message.PositionCode);
        Assert.Equal(position.OrgUnitId, message.OrgUnitId);
        Assert.Equal(position.Id, message.FundedPositionId);
        Assert.False(message.IsInternalMobility);

        // The regime id now also reaches IEmployeeCreated for a new hire (the record is read from the change tracker).
        Assert.Equal("law50", Prop(published[1].Values, nameof(IEmployeeCreated.LegalRegimeId)));
    }

    [Fact]
    public async Task Internal_mobility_publishes_only_internal_placement_completed()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (winner, existing) = await SeedInternalMobilityAsync(db);
        var publish = CreatePublishMock();

        await CreateClosure(db, publish.Object).HireExistingEmployeeAsync(winner, existing.Id, Start, "PERMANENT");

        // HCM-032: besides the hire event only the requisition-closed aggregate (the last vacancy was filled).
        var all = Published(publish);
        Assert.Contains(all, p => p.Type == typeof(IRecruitmentRequisitionClosed));
        var published = Assert.Single(all, p => p.Type != typeof(IRecruitmentRequisitionClosed));
        Assert.Equal(typeof(IInternalPlacementCompleted), published.Type);
        Assert.Equal(existing.Id, Prop(published.Values, nameof(IInternalPlacementCompleted.EmployeeId)));
        Assert.Equal(RecruitmentApplicationStatus.Hired,
            (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == winner.Id)).Status);
    }

    [Fact]
    public async Task V3_shaped_message_is_read_through_the_v4_interface_with_defaults()
    {
        var employeeId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        var message = await RoundTripAsync(new
        {
            EmployeeId = employeeId,
            EmploymentCategory = "PERMANENT",
            ExpectedStartDate = DateTime.UtcNow.Date,
            FullName = "مرشح v3",
            SourceSystem = "HR.Core",
            CorrelationId = Guid.NewGuid().ToString(),
            RecruitmentApplicationId = applicationId,
            SchemaVersion = 3
        });

        Assert.Equal(employeeId, message.EmployeeId);
        Assert.Equal(applicationId, message.RecruitmentApplicationId);
        Assert.Equal("PERMANENT", message.EmploymentCategory);
        Assert.Equal(3, message.SchemaVersion);
        Assert.Null(message.OfferedSalary);
        Assert.Null(message.GradeCode);
        Assert.Null(message.LegalRegimeId);
        Assert.Null(message.PayrollRegimeCode);
        Assert.Null(message.EmploymentType);
        Assert.Null(message.PositionCode);
        Assert.Null(message.OrgUnitId);
        Assert.False(message.IsInternalMobility);
    }

    private static async Task<ICandidateAccepted> RoundTripAsync(object values)
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<CaptureConsumer>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        // QA-034 (D-QA-14, root cause): Consumed.Any() returns false once the bus has been idle for the harness's
        // inactivity window. In the full unit run the thread pool is saturated, the in-memory delivery can start later
        // than that window and the round trip failed after ~22 s ("flaky"). The assertion is unchanged; the harness
        // only waits long enough for a loaded machine.
        harness.TestInactivityTimeout = TimeSpan.FromSeconds(30);
        harness.TestTimeout = TimeSpan.FromSeconds(90);
        await harness.Start();
        try
        {
            await harness.Bus.Publish<ICandidateAccepted>(values);
            Assert.True(await harness.GetConsumerHarness<CaptureConsumer>().Consumed.Any<ICandidateAccepted>());
            return harness.GetConsumerHarness<CaptureConsumer>().Consumed.Select<ICandidateAccepted>().First().Context.Message;
        }
        finally
        {
            await harness.Stop();
        }
    }

    private static async Task<(RecruitmentApplication Winner, Employee Existing)> SeedInternalMobilityAsync(
        HR.Core.Infrastructure.Data.HRCoreDbContext db)
    {
        var (winner, req) = await Hcm027ReserveListTests.SeedAcceptedOfferAsync(db);
        var targetPosition = await db.FundedPositions.SingleAsync(p => p.Id == req.FundedPositionId);
        var existing = await SeedEmployeeAsync(db, winner.NationalId!, "موظف متنقل");
        existing.PrimaryOrgUnitId = targetPosition.OrgUnitId;

        var vacancy = RecruitmentRequisitionService.MapToInternalVacancy(req, targetPosition);
        db.InternalVacancies.Add(vacancy);
        req.InternalVacancy = vacancy;
        req.InternalVacancyId = vacancy.Id;
        var internalApp = new InternalApplication
        {
            InternalVacancyId = vacancy.Id,
            EmployeeId = existing.Id,
            RequisitionId = req.Id,
            Status = InternalApplicationStatus.Shortlisted,
            AppliedAt = DateTime.UtcNow
        };
        db.InternalApplications.Add(internalApp);
        winner.SourceInternalApplicationId = internalApp.Id;
        await db.SaveChangesAsync();
        return (winner, existing);
    }

    private sealed class CaptureConsumer : IConsumer<ICandidateAccepted>
    {
        public Task Consume(ConsumeContext<ICandidateAccepted> context) => Task.CompletedTask;
    }

    /// <summary>A v3 implementation: no v4 members, so the defaults apply.</summary>
    private sealed record V3Stub(
        Guid EmployeeId,
        string EmploymentCategory,
        DateTime ExpectedStartDate,
        string? FullName,
        string SourceSystem,
        string? CorrelationId) : ICandidateAccepted;
}
