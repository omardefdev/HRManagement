using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dev.Analytics.Infrastructure.Consumers;

/// <summary>
/// HCM-032 (PLAN-32, P6): stores HR.Core's requisition close aggregates as <see cref="RecruitmentRequisitionFact"/>,
/// idempotent per requisition (unique per tenant): a redelivery updates the same row with the same values. Additive —
/// the demo data and the analytics events are untouched.
/// </summary>
public class RecruitmentRequisitionClosedConsumer(AnalyticsDbContext db, ILogger<RecruitmentRequisitionClosedConsumer> logger)
    : IConsumer<IRecruitmentRequisitionClosed>
{
    public async Task Consume(ConsumeContext<IRecruitmentRequisitionClosed> context)
    {
        var m = context.Message;
        var fact = await db.RecruitmentRequisitionFacts
            .FirstOrDefaultAsync(f => f.TenantId == m.TenantId && f.RequisitionId == m.RequisitionId, context.CancellationToken);
        if (fact is null)
        {
            fact = new RecruitmentRequisitionFact { TenantId = m.TenantId, RequisitionId = m.RequisitionId };
            db.RecruitmentRequisitionFacts.Add(fact);
        }

        fact.OrgUnitId = m.OrgUnitId;
        fact.Outcome = m.Outcome == "Cancelled" ? "Cancelled" : "Closed";
        fact.ClosedAt = m.ClosedAt == default ? DateTime.UtcNow : m.ClosedAt;
        fact.Applicants = m.Applicants;
        fact.HiredCount = m.HiredCount;
        fact.TimeToFillDays = m.TimeToFillDays;
        fact.ApplicantsPublic = m.ApplicantsPublic;
        fact.ApplicantsInternal = m.ApplicantsInternal;
        fact.ApplicantsHrEntered = m.ApplicantsHrEntered;
        fact.ApplicantsLegacy = m.ApplicantsLegacy;
        fact.OffersAccepted = m.OffersAccepted;
        fact.OffersDeclined = m.OffersDeclined;
        fact.OffersExpired = m.OffersExpired;
        fact.OffersRevoked = m.OffersRevoked;
        fact.SchemaVersion = m.SchemaVersion;

        logger.LogInformation("Dev.Analytics: RecruitmentRequisitionClosed RequisitionId={RequisitionId} Outcome={Outcome}",
            m.RequisitionId, fact.Outcome);
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
