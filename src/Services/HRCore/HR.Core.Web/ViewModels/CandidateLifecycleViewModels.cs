using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel.ViewModels;

namespace HR.Core.Web.ViewModels;

/// <summary>HCM-030 (G4): the candidate retention screen (due list, actions, run history) of the admin's tenant.</summary>
public sealed class CandidateLifecycleIndexViewModel
{
    public PaginatedList<CandidateRetentionDueItem> Due { get; init; } = PaginatedList<CandidateRetentionDueItem>.Empty();
    public CandidateRetentionCounts Counts { get; init; } = new(0, 0, 0);
    public IReadOnlyList<CandidateRetentionRun> Runs { get; init; } = [];
    public int RetentionMonths { get; init; }

    /// <summary><c>Recruitment:Retention:Apply</c>: «تنفيذ الإخفاء» is offered only when true.</summary>
    public bool ApplyEnabled { get; init; }

    public bool JobEnabled { get; init; }

    public static string TriggerLabel(CandidateRetentionTriggerKind kind) => kind switch
    {
        CandidateRetentionTriggerKind.RequisitionClosed => "إغلاق الشاغر",
        CandidateRetentionTriggerKind.RequisitionCancelled => "إلغاء الشاغر",
        _ => "تاريخ التقديم"
    };

    public static string ReasonLabel(CandidateRetentionDueItem item, int months)
    {
        var reason = $"انقضت مدة الاحتفاظ ({months} شهراً) من {TriggerLabel(item.TriggerKind)}";
        if (item.IsLegacyCitizen)
            return reason + " — طلب بوابة قديم غير مرتبط";
        return item.PartiallyAnonymized ? reason + " — إكمال إخفاء جزئي" : reason;
    }
}
