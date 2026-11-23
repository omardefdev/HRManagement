using HRManagement.ServiceDefaults;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.PayrollConfigurations;

/// <summary>Why a framework command was refused, and so its HTTP answer.</summary>
public enum PayrollFrameworkFailure
{
    None = 0,

    /// <summary>A broken rule of the pack, of the request or of the cycle: 400.</summary>
    Invalid = 1,

    /// <summary>The version changed under the request (another person's write): 409.</summary>
    Conflict = 2,

    /// <summary>No such version in the caller's agency: 404.</summary>
    NotFound = 3,
}

/// <summary>
/// The answer of every framework command. A refusal changes nothing: <see cref="Status"/> is then the version's unchanged status,
/// <see cref="Error"/> an Arabic message, and <see cref="Errors"/> each broken rule of a pack with the field and the row it names.
/// </summary>
public sealed record PayrollFrameworkResult(
    bool Success,
    Guid? FrameworkId,
    PayrollConfigPublicationStatus? Status,
    string? Error,
    PayrollFrameworkFailure Failure = PayrollFrameworkFailure.None)
{
    public IReadOnlyList<PackError> Errors { get; init; } = [];

    public int? VersionNumber { get; init; }

    public string? Code { get; init; }

    public string? ContentHash { get; init; }

    public static PayrollFrameworkResult Ok(PayrollConfiguration version) =>
        new(true, version.Id, version.PublicationStatus, null)
        {
            VersionNumber = version.VersionNumber,
            Code = version.Code,
            ContentHash = version.ContentHash,
        };

    public static PayrollFrameworkResult Invalid(string error, PayrollConfiguration? version = null) =>
        new(false, version?.Id, version?.PublicationStatus, error, PayrollFrameworkFailure.Invalid);

    public static PayrollFrameworkResult InvalidPack(IReadOnlyList<PackError> errors) =>
        new(false, null, null, PayrollFrameworkImportMessages.PackRefused(errors.Count), PayrollFrameworkFailure.Invalid) { Errors = errors };

    public static PayrollFrameworkResult Conflict(string error, PayrollConfiguration? version = null) =>
        new(false, version?.Id, version?.PublicationStatus, error, PayrollFrameworkFailure.Conflict);

    public static PayrollFrameworkResult NotFound() =>
        new(false, null, null, PayrollFrameworkMessages.NotFound, PayrollFrameworkFailure.NotFound);

    public int StatusCode => Failure switch
    {
        PayrollFrameworkFailure.None => 200,
        PayrollFrameworkFailure.Conflict => 409,
        PayrollFrameworkFailure.NotFound => 404,
        _ => 400,
    };
}

public static class PayrollFrameworkImportMessages
{
    public const string CodeInvalid = "رمز الإطار: أحرف لاتينية كبيرة وأرقام وشرطة سفلية، من 3 إلى 100 حرف";
    public const string CodeOfAnotherRegime = "رمز الإطار مستعمل لنظام قانوني آخر في هذه الجهة؛ اختر رمزاً آخر";
    public const string TargetNotDraft = "لا يُستبدل بالاستيراد إلا محتوى مسودة";
    public const string TargetOfAnotherCode = "المسودة المستهدفة تحمل رمزاً آخر غير رمز الحزمة";
    public const string OneSource = "اختر مصدراً واحداً للمسودة: قالب نظام قانوني (regimeCode) أو استنساخ نسخة (cloneFromId)";
    public const string UnknownRegime = "النظام القانوني غير معروف";
    public const string SourceNotFound = "النسخة المطلوب استنساخها غير موجودة";
    public const string ChangedConcurrently = "تغيّرت نسخ هذا الرمز للتو من مستخدم آخر؛ أعد المحاولة";

    public static string PackRefused(int count) =>
        FormattableString.Invariant($"رُفضت الحزمة ولم يُكتب منها شيء: {count} مخالفة لمخطط الحزمة وقواعدها");

    public static string DecisionMissing(string decisionId) =>
        $"القرار {decisionId} تستشهد به قيمة في الحزمة وليس في جدول قراراتها ولا في سجل القرارات";
}

/// <summary>
/// PLAN-54 (§1): import a legal pack as a new draft of its code (<c>VersionNumber</c> = the code's highest + 1), or replace the content
/// of an existing draft (<paramref name="TargetDraftId"/>). <paramref name="CodeOverride"/> and <paramref name="EffectiveFromOverride"/>
/// take the place of the pack's own code and date. The actor becomes an editor of the version.
/// </summary>
public sealed record ImportPayrollFrameworkPackCommand(
    string? PackJson,
    string? CodeOverride,
    DateOnly? EffectiveFromOverride,
    Guid? TargetDraftId,
    Guid ActorId) : IRequest<PayrollFrameworkResult>;

/// <summary>
/// PLAN-54 (§1): a new draft from a legal regime's template (<paramref name="RegimeCode"/>) or as a clone of a version
/// (<paramref name="CloneFromId"/>), with the effective date the user gives. The actor becomes an editor of the draft.
/// </summary>
public sealed record CreatePayrollFrameworkCommand(
    string? RegimeCode,
    Guid? CloneFromId,
    string? Code,
    string? NameAr,
    DateOnly? EffectiveFrom,
    Guid ActorId) : IRequest<PayrollFrameworkResult>;

/// <summary>PLAN-54 (§1): the effective date of a draft. The date belongs to the version; only a draft changes it.</summary>
public sealed record SetPayrollFrameworkEffectiveFromCommand(Guid FrameworkId, DateOnly? EffectiveFrom, Guid ActorId)
    : IRequest<PayrollFrameworkResult>;

/// <summary>PLAN-54 (§1): a version as a pack (JSON), with the decisions its values cite. Null when the version does not exist.</summary>
public sealed record ExportPayrollFrameworkQuery(Guid FrameworkId) : IRequest<PayrollFrameworkExport?>;

public sealed record PayrollFrameworkExport(string Json, string FileName);

/// <summary>What the framework handlers share: reading a code's versions, and what a posted run forbids.</summary>
public static class PayrollFrameworkQueries
{
    public static DateTime Day(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public static async Task<int> NextVersionNumberAsync(IPayrollRepository repository, string code, CancellationToken ct) =>
        (await repository.PayrollConfigurations.Where(c => c.Code == code).MaxAsync(c => (int?)c.VersionNumber, ct) ?? 0) + 1;

    /// <summary>A code keeps one legal regime through all of its versions.</summary>
    public static Task<bool> CodeBelongsToAnotherRegimeAsync(IPayrollRepository repository, string code, string regimeCode, CancellationToken ct) =>
        repository.PayrollConfigurations.AnyAsync(c => c.Code == code && c.RegimeCode != regimeCode, ct);

    /// <summary>
    /// D-PAY-13: the latest period end of the posted runs of <paramref name="regimeCode"/>. A run's regime is its pinned version's; a
    /// posted run pinned to no version (the Demo seed's) counts for every regime.
    /// </summary>
    /// <summary>
    /// Another person changed the same version, or the same code's versions, between this request's read and its save: the row
    /// version of the version moved (PLAN-54: an approval fixes the content it fingerprinted), or a unique index refused the row.
    /// </summary>
    public static bool IsConcurrentChange(DbUpdateException ex) =>
        ex is DbUpdateConcurrencyException || ConcurrencyExtensions.IsUniqueViolation(ex);

    /// <summary>Saves; a concurrent change writes nothing and is answered as a conflict (null when saved).</summary>
    public static async Task<PayrollFrameworkResult?> SaveAsync(IPayrollRepository repository, CancellationToken ct)
    {
        try
        {
            await repository.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (IsConcurrentChange(ex))
        {
            repository.ClearChangeTracker();
            return PayrollFrameworkResult.Conflict(PayrollFrameworkImportMessages.ChangedConcurrently);
        }
    }

    public static async Task<DateTime?> LatestPostedPeriodEndAsync(IPayrollRepository repository, string regimeCode, CancellationToken ct)
    {
        var versionsOfRegime = repository.PayrollConfigurations.Where(c => c.RegimeCode == regimeCode).Select(c => (Guid?)c.Id);
        return await repository.PayrollRuns
            .Where(r => r.Status == PayrollRunStatus.Posted
                        && (r.PayrollConfigurationId == null || versionsOfRegime.Contains(r.PayrollConfigurationId)))
            .MaxAsync(r => (DateTime?)r.PeriodEnd, ct);
    }

    /// <summary>What a run of a version older than PLAN-54 reads from the general tables (for the v2 draft cloned from it).</summary>
    public static async Task<PayrollLegacyReferenceData> LegacyReferenceDataAsync(IPayrollRepository repository, CancellationToken ct) =>
        new(
            await repository.DeductionTypes.AsNoTracking().Where(t => t.IsStatutory).OrderBy(t => t.Code).ToListAsync(ct),
            await repository.OfficialHolidays.AsNoTracking().Where(h => h.PayrollConfigurationId == null).ToListAsync(ct),
            await repository.DisbursementPriorityRules.AsNoTracking()
                .Where(r => r.IsActive && r.PayrollConfigurationId == null)
                .ToListAsync(ct));
}

public sealed class ImportPayrollFrameworkPackHandler(IPayrollRepository repository, IPayrollCalendar calendar)
    : IRequestHandler<ImportPayrollFrameworkPackCommand, PayrollFrameworkResult>
{
    public async Task<PayrollFrameworkResult> Handle(ImportPayrollFrameworkPackCommand request, CancellationToken cancellationToken)
    {
        var (pack, readError) = PayrollFrameworkPackReader.Read(request.PackJson);
        if (pack is null)
            return PayrollFrameworkResult.InvalidPack([readError! with { Path = PackContract.SafePath(readError.Path) }]);

        // The overrides take the place of the pack's own code and date before the rules are checked.
        if (request.CodeOverride is { } codeOverride)
        {
            if (!PayrollFrameworkPackValidator.IsValidCode(codeOverride))
                return PayrollFrameworkResult.Invalid(PayrollFrameworkImportMessages.CodeInvalid);
            pack.Code = codeOverride;
        }

        if (request.EffectiveFromOverride is { } dateOverride)
            pack.EffectiveFrom = dateOverride.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var errors = PayrollFrameworkPackValidator.Validate(pack);
        if (errors.Count > 0)
            return PayrollFrameworkResult.InvalidPack(errors);

        var code = pack.Code!;
        PayrollFrameworkPackValidator.TryParseDate(pack.EffectiveFrom, out var effectiveFrom);

        // Rule 4: every decision a value cites exists after the import: in the pack, or already in the agency's register.
        var packDecisions = pack.Decisions!.Select(d => d.DecisionId!).ToHashSet(StringComparer.Ordinal);
        var cited = PayrollFrameworkPackValidator.CitedDecisions(pack);
        var outside = cited.Select(c => c.DecisionId).Where(id => !packDecisions.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
        if (outside.Count > 0)
        {
            var registered = await repository.LegalDecisionRecords.AsNoTracking()
                .Where(d => outside.Contains(d.DecisionId))
                .Select(d => d.DecisionId)
                .ToListAsync(cancellationToken);
            var missing = cited
                .Where(c => !packDecisions.Contains(c.DecisionId) && !registered.Contains(c.DecisionId))
                .Select(c => new PackError(c.Path, PayrollFrameworkImportMessages.DecisionMissing(c.DecisionId)))
                .Take(PayrollFrameworkPackValidator.MaxErrors)
                .ToList();
            if (missing.Count > 0)
                return PayrollFrameworkResult.InvalidPack(missing);
        }

        if (await PayrollFrameworkQueries.CodeBelongsToAnotherRegimeAsync(repository, code, pack.RegimeCode!, cancellationToken))
            return PayrollFrameworkResult.Invalid(PayrollFrameworkImportMessages.CodeOfAnotherRegime);

        PayrollConfiguration? target = null;
        if (request.TargetDraftId is Guid targetId)
        {
            target = await repository.GetFrameworkForUpdateAsync(targetId, cancellationToken);
            if (target is null)
                return PayrollFrameworkResult.NotFound();
            if (target.PublicationStatus != PayrollConfigPublicationStatus.Draft)
                return PayrollFrameworkResult.Conflict(PayrollFrameworkImportMessages.TargetNotDraft, target);
            if (target.Code != code)
                return PayrollFrameworkResult.Invalid(PayrollFrameworkImportMessages.TargetOfAnotherCode, target);
        }

        // Rule 3: one transaction. A pack refused at any point from here leaves no row: not a decision, not a draft, not a child row.
        await using var transaction = await repository.BeginTransactionAsync(cancellationToken);
        try
        {
            var now = calendar.UtcNow;
            var existingDecisions = await repository.LegalDecisionRecords
                .Where(d => packDecisions.Contains(d.DecisionId))
                .ToListAsync(cancellationToken);
            foreach (var decision in pack.Decisions!)
            {
                var row = existingDecisions.FirstOrDefault(d => d.DecisionId == decision.DecisionId);
                if (row is null)
                {
                    row = new LegalDecisionRecord();
                    PayrollFrameworkPackMapper.ApplyDecision(decision, row);
                    await repository.AddAsync(row, cancellationToken);
                }
                else if (!PayrollFrameworkPackMapper.DecisionMatches(decision, row))
                {
                    // A register row the pack repeats as it is stays untouched: importing the same pack again writes no decision.
                    PayrollFrameworkPackMapper.ApplyDecision(decision, row);
                    row.UpdatedAt = now;
                }
            }

            PayrollConfiguration draft;
            if (target is not null)
            {
                draft = target;
                // The old rows are deleted row by row: taking a holiday or a priority rule out of the collection would only clear its
                // optional link to the version and leave a general row behind, which every legacy run reads.
                foreach (var row in draft.TaxBrackets.ToList()) repository.Remove(row);
                foreach (var row in draft.StatutoryDeductions.ToList()) repository.Remove(row);
                foreach (var row in draft.Holidays.ToList()) repository.Remove(row);
                foreach (var row in draft.PriorityRules.ToList()) repository.Remove(row);
                foreach (var row in draft.FieldSources.ToList()) repository.Remove(row);
                await repository.SaveChangesAsync(cancellationToken);
                draft.TaxBrackets.Clear();
                draft.StatutoryDeductions.Clear();
                draft.Holidays.Clear();
                draft.PriorityRules.Clear();
                draft.FieldSources.Clear();
                draft.HashSchemaVersion = PayrollConfiguration.CurrentHashSchemaVersion;
                draft.UpdatedAt = now;
            }
            else
            {
                draft = new PayrollConfiguration
                {
                    Code = code,
                    VersionNumber = await PayrollFrameworkQueries.NextVersionNumberAsync(repository, code, cancellationToken),
                    PublicationStatus = PayrollConfigPublicationStatus.Draft,
                    HashSchemaVersion = PayrollConfiguration.CurrentHashSchemaVersion,
                    IsActive = false,
                };
            }

            draft.EffectiveFrom = PayrollFrameworkQueries.Day(effectiveFrom);
            draft.EffectiveTo = null;
            draft.ContentHash = null;
            PayrollFrameworkPackMapper.ApplyContent(pack, draft);
            var editorsBefore = draft.Editors.Count;
            draft.RecordEditor(request.ActorId, PayrollFrameworkEditKind.Imported, now);

            if (target is null)
            {
                await repository.AddAsync(draft, cancellationToken);
            }
            else
            {
                // Rows added to a tracked draft's collections are new rows, whatever key they were given.
                foreach (var row in draft.TaxBrackets) repository.MarkAsAdded(row);
                foreach (var row in draft.StatutoryDeductions) repository.MarkAsAdded(row);
                foreach (var row in draft.Holidays) repository.MarkAsAdded(row);
                foreach (var row in draft.PriorityRules) repository.MarkAsAdded(row);
                foreach (var row in draft.FieldSources) repository.MarkAsAdded(row);
                foreach (var row in draft.Editors.Skip(editorsBefore))
                    repository.MarkAsAdded(row);
            }

            await repository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PayrollFrameworkResult.Ok(draft);
        }
        catch (DbUpdateException ex) when (PayrollFrameworkQueries.IsConcurrentChange(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            repository.ClearChangeTracker();
            return PayrollFrameworkResult.Conflict(PayrollFrameworkImportMessages.ChangedConcurrently);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            repository.ClearChangeTracker();
            throw;
        }
    }
}

public sealed class CreatePayrollFrameworkHandler(IPayrollRepository repository, IPayrollCalendar calendar)
    : IRequestHandler<CreatePayrollFrameworkCommand, PayrollFrameworkResult>
{
    public async Task<PayrollFrameworkResult> Handle(CreatePayrollFrameworkCommand request, CancellationToken cancellationToken)
    {
        var fromTemplate = !string.IsNullOrWhiteSpace(request.RegimeCode);
        if (fromTemplate == request.CloneFromId.HasValue)
            return PayrollFrameworkResult.Invalid(PayrollFrameworkImportMessages.OneSource);
        if (request.EffectiveFrom is not { } date || date == default)
            return PayrollFrameworkResult.Invalid(PayrollFrameworkMessages.EffectiveFromRequired);

        var effectiveFrom = PayrollFrameworkQueries.Day(date);
        PayrollConfiguration draft;
        if (request.CloneFromId is Guid sourceId)
        {
            var source = await repository.GetConfigurationByIdAsync(sourceId, cancellationToken);
            if (source is null)
                return PayrollFrameworkResult.Invalid(PayrollFrameworkImportMessages.SourceNotFound);

            var legacy = source.IsLegacyFramework
                ? await PayrollFrameworkQueries.LegacyReferenceDataAsync(repository, cancellationToken)
                : null;
            draft = PayrollConfigurationVersioning.CloneAsDraft(
                source,
                await PayrollFrameworkQueries.NextVersionNumberAsync(repository, source.Code, cancellationToken),
                effectiveFrom,
                legacy: legacy);
            // A copy of a version nobody has published yet still holds its authors' content: they stay its editors, so the author
            // of a draft cannot approve a colleague's copy of it. A published version's content was approved and attested.
            if (source.PublicationStatus is PayrollConfigPublicationStatus.Draft or PayrollConfigPublicationStatus.Approved)
            {
                var authors = await repository.PayrollConfigurations.AsNoTracking()
                    .Where(c => c.Id == sourceId)
                    .SelectMany(c => c.Editors)
                    .Select(e => new { e.UserId, e.Kind, e.At })
                    .ToListAsync(cancellationToken);
                foreach (var author in authors)
                    draft.RecordEditor(author.UserId, author.Kind, author.At);
            }
            if (!string.IsNullOrWhiteSpace(request.NameAr))
                draft.NameAr = request.NameAr.Trim();
        }
        else
        {
            var regime = request.RegimeCode!.Trim();
            if (!PayrollFrameworkTemplate.IsKnownRegime(regime))
                return PayrollFrameworkResult.Invalid(PayrollFrameworkImportMessages.UnknownRegime);
            var code = string.IsNullOrWhiteSpace(request.Code) ? regime : request.Code.Trim();
            if (!PayrollFrameworkPackValidator.IsValidCode(code))
                return PayrollFrameworkResult.Invalid(PayrollFrameworkImportMessages.CodeInvalid);
            if (await PayrollFrameworkQueries.CodeBelongsToAnotherRegimeAsync(repository, code, regime, cancellationToken))
                return PayrollFrameworkResult.Invalid(PayrollFrameworkImportMessages.CodeOfAnotherRegime);

            draft = PayrollFrameworkTemplate.CreateDraft(
                regime,
                code,
                request.NameAr,
                await PayrollFrameworkQueries.NextVersionNumberAsync(repository, code, cancellationToken),
                effectiveFrom);
        }

        if (draft.NameAr.Length > 200)
            return PayrollFrameworkResult.Invalid("اسم الإطار لا يتجاوز 200 حرف");

        draft.RecordEditor(request.ActorId, PayrollFrameworkEditKind.Created, calendar.UtcNow);
        await repository.AddAsync(draft, cancellationToken);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PayrollFrameworkQueries.IsConcurrentChange(ex))
        {
            repository.ClearChangeTracker();
            return PayrollFrameworkResult.Conflict(PayrollFrameworkImportMessages.ChangedConcurrently);
        }

        return PayrollFrameworkResult.Ok(draft);
    }
}

public sealed class SetPayrollFrameworkEffectiveFromHandler(IPayrollRepository repository, IPayrollCalendar calendar)
    : IRequestHandler<SetPayrollFrameworkEffectiveFromCommand, PayrollFrameworkResult>
{
    public async Task<PayrollFrameworkResult> Handle(SetPayrollFrameworkEffectiveFromCommand request, CancellationToken cancellationToken)
    {
        if (request.EffectiveFrom is not { } date || date == default)
            return PayrollFrameworkResult.Invalid(PayrollFrameworkMessages.EffectiveFromRequired);

        var draft = await repository.GetFrameworkForUpdateAsync(request.FrameworkId, cancellationToken);
        if (draft is null)
            return PayrollFrameworkResult.NotFound();
        if (draft.PublicationStatus != PayrollConfigPublicationStatus.Draft)
            return PayrollFrameworkResult.Conflict(PayrollFrameworkMessages.DraftOnly, draft);

        var before = draft.Editors.Count;
        draft.EffectiveFrom = PayrollFrameworkQueries.Day(date);
        draft.ContentHash = null;
        draft.UpdatedAt = calendar.UtcNow;
        draft.RecordEditor(request.ActorId, PayrollFrameworkEditKind.Edited, calendar.UtcNow);
        foreach (var added in draft.Editors.Skip(before))
            repository.MarkAsAdded(added);
        return await PayrollFrameworkQueries.SaveAsync(repository, cancellationToken) ?? PayrollFrameworkResult.Ok(draft);
    }
}

public sealed class ExportPayrollFrameworkHandler(IPayrollRepository repository)
    : IRequestHandler<ExportPayrollFrameworkQuery, PayrollFrameworkExport?>
{
    public async Task<PayrollFrameworkExport?> Handle(ExportPayrollFrameworkQuery request, CancellationToken cancellationToken)
    {
        var version = await repository.GetConfigurationByIdAsync(request.FrameworkId, cancellationToken);
        if (version is null)
            return null;

        var cited = PayrollFrameworkPackMapper.CitedDecisionIds(version);
        var decisions = cited.Count == 0
            ? []
            : await repository.LegalDecisionRecords.AsNoTracking().Where(d => cited.Contains(d.DecisionId)).ToListAsync(cancellationToken);
        var json = PayrollFrameworkPackReader.Write(PayrollFrameworkPackMapper.ToPack(version, decisions));
        return new PayrollFrameworkExport(json, FormattableString.Invariant($"{version.Code}-v{version.VersionNumber}.json"));
    }
}
