using System.Security.Cryptography;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>HCM-031: the marks of one paper by round (null = not submitted).</summary>
public sealed record PaperMarks(decimal? First, decimal? Second, decimal? Reconciler, decimal? ObjectionCorrection = null);

/// <summary>HCM-031: where one paper stands in the double-marking cycle.</summary>
public enum PaperMarkingState
{
    AwaitingMarks = 0,
    AwaitingReconciler = 1,
    Complete = 2
}

/// <summary>HCM-031: input of an exam session (validated by <see cref="ExamMarkingRules.ValidateSession"/>).</summary>
public sealed record ExamSessionInput(
    ExamSessionType Type,
    string? Title,
    DateTime? ScheduledAt,
    int DurationMinutes,
    string? Venue,
    int Capacity,
    decimal MaxScore);

/// <summary>HCM-031: one seat of a seating plan.</summary>
public sealed record ExamSeat(Guid ApplicationId, Guid SessionId, int SeatNumber);

/// <summary>
/// HCM-031 (PLAN-31, AC-HCM-31b/31c): pure rules of seating and anonymous double marking. Within the reconciliation
/// threshold the exam mark is the average of the first and second marks; above it a reconciler marks the paper and the
/// competition's <see cref="CompetitionReconcileRule"/> decides (PLAN-31 J12). An objection correction (P4) replaces the mark.
/// </summary>
public static class ExamMarkingRules
{
    public const int ExamNumberLength = 8;
    public const int MaxDurationMinutes = 600;
    public const int MaxNoteLength = 500;

    public const string SessionLockedMessage = "أُغلق التصحيح لهذه الجلسة؛ لا يمكن تعديل العلامات.";
    public const string NotAssignedMessage = "لست مكلفاً بتصحيح هذه الجلسة.";
    public const string AbsentMessage = "المتقدم غائب؛ لا تُرصد له علامة.";
    public const string ReconcilerNotNeededMessage = "لا يحتاج هذا الدفتر إلى مصحح ثالث.";
    public const string SamePersonMessage = "لا يجوز أن يصحح الشخص نفسه الدفتر في أكثر من جولة.";

    public static string? ValidateSession(ExamSessionInput input, decimal reconcileThreshold)
    {
        if (!Enum.IsDefined(input.Type))
            return "نوع الامتحان غير معروف.";
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 200)
            return "عنوان الجلسة مطلوب (200 حرف على الأكثر).";
        if (input.ScheduledAt is null)
            return "موعد الجلسة مطلوب.";
        if (input.DurationMinutes is < 1 or > MaxDurationMinutes)
            return $"مدة الجلسة يجب أن تكون بين 1 و{MaxDurationMinutes} دقيقة.";
        if (string.IsNullOrWhiteSpace(input.Venue) || input.Venue.Trim().Length > 300)
            return "مكان الجلسة مطلوب (300 حرف على الأكثر).";
        if (input.Capacity < 1)
            return "سعة الجلسة يجب أن تكون 1 على الأقل.";
        if (input.MaxScore <= 0)
            return "العلامة العظمى يجب أن تكون أكبر من صفر.";
        if (reconcileThreshold >= input.MaxScore)
            return $"عتبة التصحيح الثالث للمسابقة ({reconcileThreshold:0.##}) يجب أن تكون أقل من العلامة العظمى ({input.MaxScore:0.##}).";
        return null;
    }

    public static string? ValidateScore(decimal score, decimal maxScore) =>
        score < 0 || score > maxScore ? $"العلامة يجب أن تكون بين 0 و{maxScore:0.##}." : null;

    public static ExamScoreRound RoundFor(ExamMarkerRole role) => role switch
    {
        ExamMarkerRole.First => ExamScoreRound.First,
        ExamMarkerRole.Second => ExamScoreRound.Second,
        _ => ExamScoreRound.Reconciler
    };

    public static bool NeedsReconciliation(decimal first, decimal second, decimal threshold) => Math.Abs(first - second) > threshold;

    public static PaperMarkingState State(PaperMarks marks, decimal threshold)
    {
        if (marks.ObjectionCorrection is not null)
            return PaperMarkingState.Complete;
        if (marks.First is not decimal a || marks.Second is not decimal b)
            return PaperMarkingState.AwaitingMarks;
        if (!NeedsReconciliation(a, b, threshold))
            return PaperMarkingState.Complete;
        return marks.Reconciler is null ? PaperMarkingState.AwaitingReconciler : PaperMarkingState.Complete;
    }

    /// <summary>The exam mark of a complete paper (rounded to 2 places, away from zero); null while the paper is not complete.</summary>
    public static decimal? ComputeMark(PaperMarks marks, decimal threshold, CompetitionReconcileRule rule)
    {
        if (marks.ObjectionCorrection is decimal corrected)
            return Round(corrected);
        if (State(marks, threshold) != PaperMarkingState.Complete)
            return null;
        var a = marks.First!.Value;
        var b = marks.Second!.Value;
        if (!NeedsReconciliation(a, b, threshold))
            return Round((a + b) / 2m);
        var c = marks.Reconciler!.Value;
        return rule switch
        {
            CompetitionReconcileRule.Average => Round((a + b + c) / 3m),
            CompetitionReconcileRule.ReconcilerDecides => Round(c),
            _ => Round(ClosestPairAverage(a, b, c))
        };
    }

    /// <summary>
    /// Blindness: the first and second markers never see another mark; the reconciler sees the first and second marks
    /// only once both are submitted. Returns the marks the viewer may see (others null).
    /// </summary>
    public static PaperMarks VisibleMarks(ExamMarkerRole viewerRole, PaperMarks marks) =>
        viewerRole == ExamMarkerRole.Reconciler && marks.First is not null && marks.Second is not null
            ? marks with { ObjectionCorrection = null }
            : new PaperMarks(null, null, null);

    /// <summary>Distinct people per role on a session (one person holds one role; one person per role).</summary>
    public static string? ValidateAssignment(
        IReadOnlyCollection<(Guid EmployeeId, ExamMarkerRole Role)> existing,
        Guid employeeId,
        ExamMarkerRole role)
    {
        if (!Enum.IsDefined(role))
            return "دور المصحح غير معروف.";
        if (existing.Any(e => e.Role == role))
            return "يوجد مصحح مكلف بهذا الدور في الجلسة.";
        if (existing.Any(e => e.EmployeeId == employeeId))
            return "هذا الموظف مكلف بدور آخر في الجلسة نفسها؛ يجب أن يكون المصححون أشخاصاً مختلفين.";
        return null;
    }

    /// <summary>A cryptographically random exam number of <see cref="ExamNumberLength"/> digits (first digit 1–9).</summary>
    public static string NewExamNumber()
    {
        Span<char> digits = stackalloc char[ExamNumberLength];
        digits[0] = (char)('1' + RandomNumberGenerator.GetInt32(9));
        for (var i = 1; i < ExamNumberLength; i++)
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return new string(digits);
    }

    /// <summary>
    /// Seating plan: applicants ordered by exam number fill the sessions in order (by schedule, then id) up to their
    /// capacity; seat numbers start at 1 in every session. Error (Arabic, with the shortfall) when the capacity is short.
    /// </summary>
    public static (IReadOnlyList<ExamSeat> Seats, string? Error) PlanSeating(
        IReadOnlyList<(Guid SessionId, int Capacity)> orderedSessions,
        IReadOnlyList<(Guid ApplicationId, string ExamNumber)> applicants,
        string typeLabel)
    {
        var capacity = orderedSessions.Sum(s => s.Capacity);
        if (orderedSessions.Count == 0)
            return ([], $"لا توجد جلسات امتحان {typeLabel} للتوزيع.");
        if (capacity < applicants.Count)
            return ([], $"السعة الإجمالية لجلسات الامتحان {typeLabel} ({capacity}) أقل من عدد المقبولين ({applicants.Count}) بفارق {applicants.Count - capacity}.");

        var seats = new List<ExamSeat>(applicants.Count);
        var ordered = applicants.OrderBy(a => a.ExamNumber, StringComparer.Ordinal).ToList();
        var index = 0;
        foreach (var (sessionId, sessionCapacity) in orderedSessions)
            for (var seat = 1; seat <= sessionCapacity && index < ordered.Count; seat++)
                seats.Add(new ExamSeat(ordered[index++].ApplicationId, sessionId, seat));
        return (seats, null);
    }

    private static decimal ClosestPairAverage(decimal a, decimal b, decimal c)
    {
        var pairs = new[] { (x: a, y: b), (x: a, y: c), (x: b, y: c) };
        var best = pairs.OrderBy(p => Math.Abs(p.x - p.y)).ThenByDescending(p => p.x + p.y).First();
        return (best.x + best.y) / 2m;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
