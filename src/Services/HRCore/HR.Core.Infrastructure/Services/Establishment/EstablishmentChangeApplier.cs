using System.Text.Json;

using HR.Core.Domain.Entities;

using HR.Core.Domain.Services;

using HR.Core.Infrastructure.Data;

using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Infrastructure.Services.Recruitment;

using Microsoft.EntityFrameworkCore;



namespace HR.Core.Infrastructure.Services.Establishment;



public interface IEstablishmentChangeApplier

{

    Task<(bool Success, string? Error, Guid? EntityId, string EntityType, string? BeforeJson, string? AfterJson)>

        ApplyAsync(EstablishmentChangeRequest request, CancellationToken ct = default);

}



public sealed class EstablishmentChangeApplier(

    HRCoreDbContext db,

    EstablishmentService establishmentService,

    IFundedPositionCodeService fundedPositionCodeService,

    IJobCatalogCodeService jobCatalogCodeService,

    EstablishmentReferenceValidator referenceValidator,

    IJobCatalogVersionService jobCatalogVersions,

    IFundedPositionVersionService fundedPositionVersions,
    MassTransit.IPublishEndpoint? publish = null) : IEstablishmentChangeApplier

{

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };



    public async Task<(bool Success, string? Error, Guid? EntityId, string EntityType, string? BeforeJson, string? AfterJson)>

        ApplyAsync(EstablishmentChangeRequest request, CancellationToken ct = default)

    {

        return request.ChangeType switch

        {

            EstablishmentChangeType.CreateJobCatalog => await ApplyCreateJobCatalogAsync(request, ct),

            EstablishmentChangeType.BulkCreateJobCatalog => await ApplyBulkCreateJobCatalogAsync(request, ct),

            EstablishmentChangeType.UpdateJobCatalog => await ApplyUpdateJobCatalogAsync(request, ct),

            EstablishmentChangeType.DeactivateJobCatalog => await ApplyDeactivateJobCatalogAsync(request, ct),

            EstablishmentChangeType.CreateFundedPosition => await ApplyCreateFundedPositionAsync(request, ct),

            EstablishmentChangeType.FreezeFundedPosition => await ApplyFreezeFundedPositionAsync(request, ct),

            EstablishmentChangeType.UnfreezeFundedPosition => await ApplyUnfreezeFundedPositionAsync(request, ct),

            _ => (false, "نوع التغيير غير مدعوم.", null, string.Empty, null, null)

        };

    }



    private DateTime ResolveEffectiveFrom(EstablishmentChangeRequest request) =>

        (request.EffectiveFrom ?? DateTime.UtcNow).ToUniversalTime();



    private async Task AppendCatalogVersionAsync(

        JobCatalogEntry entry,

        EstablishmentChangeRequest request,

        CancellationToken ct)

    {

        await jobCatalogVersions.AppendFromEntryAsync(

            entry,

            EstablishmentOperationMapping.FromChangeType(request.ChangeType),

            ResolveEffectiveFrom(request),

            request.Id,

            request.LegalSourceId,

            request.DecisionReference,

            ct);

    }



    private async Task AppendPositionVersionAsync(

        FundedPosition position,

        EstablishmentChangeRequest request,

        CancellationToken ct)

    {

        await fundedPositionVersions.AppendFromPositionAsync(

            position,

            EstablishmentOperationMapping.FromChangeType(request.ChangeType),

            ResolveEffectiveFrom(request),

            request.Id,

            request.LegalSourceId,

            request.DecisionReference,

            ct);

    }



    private async Task<(bool Success, string? Error, Guid? EntityId, string EntityType, string? BeforeJson, string? AfterJson)>

        ApplyCreateJobCatalogAsync(EstablishmentChangeRequest request, CancellationToken ct)

    {

        var payload = Deserialize<JobCatalogPayload>(request.PayloadJson);

        if (payload is null) return (false, "حمولة غير صالحة.", null, nameof(JobCatalogEntry), null, null);

        var core = await CreateJobCatalogEntryCoreAsync(payload, request, ct);

        if (!core.Success) return (false, core.Error, null, nameof(JobCatalogEntry), null, null);

        db.JobCatalogEntries.Add(core.Entry!);

        await db.SaveChangesAsync(ct);

        await AppendCatalogVersionAsync(core.Entry!, request, ct);

        return (true, null, core.Entry!.Id, nameof(JobCatalogEntry), null, Serialize(core.Entry));

    }



    private async Task<(bool Success, string? Error, Guid? EntityId, string EntityType, string? BeforeJson, string? AfterJson)>

        ApplyBulkCreateJobCatalogAsync(EstablishmentChangeRequest request, CancellationToken ct)

    {

        var bulk = Deserialize<JobCatalogBulkPayload>(request.PayloadJson);

        if (bulk is null || bulk.Items.Count == 0)

            return (false, "حمولة الاستيراد الجماعي غير صالحة.", null, nameof(JobCatalogEntry), null, null);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var created = new List<JobCatalogEntry>();

        foreach (var payload in bulk.Items)

        {

            var core = await CreateJobCatalogEntryCoreAsync(payload, request, ct);

            if (!core.Success)

            {

                await transaction.RollbackAsync(ct);

                return (false, core.Error, null, nameof(JobCatalogEntry), null, null);

            }

            db.JobCatalogEntries.Add(core.Entry!);

            created.Add(core.Entry!);

        }

        await db.SaveChangesAsync(ct);

        foreach (var entry in created)

            await AppendCatalogVersionAsync(entry, request, ct);

        var batch = await db.JobCatalogImportBatches.FirstOrDefaultAsync(b => b.Id == bulk.ImportBatchId, ct);

        if (batch is not null)

        {

            batch.Status = JobCatalogImportStatus.Applied;

            await db.SaveChangesAsync(ct);

        }

        await transaction.CommitAsync(ct);

        return (true, null, created[0].Id, nameof(JobCatalogEntry), null, Serialize(created));

    }



    private async Task<(bool Success, string? Error, JobCatalogEntry? Entry)> CreateJobCatalogEntryCoreAsync(

        JobCatalogPayload payload,

        EstablishmentChangeRequest request,

        CancellationToken ct)

    {

        var effectiveAt = ResolveEffectiveFrom(request);

        if (await referenceValidator.ValidateGradeCodeAsync(payload.GradeCode, effectiveAt, ct) is { } gradeError)

            return (false, gradeError, null);

        if (await referenceValidator.ValidateJobClassificationAsync(payload.OccupationalGroup, effectiveAt, ct) is { } classError)

            return (false, classError, null);

        var jobCode = string.IsNullOrWhiteSpace(payload.JobCode)

            ? await jobCatalogCodeService.GenerateCodeAsync(payload.GradeCode, payload.OccupationalGroup, ct)

            : payload.JobCode.Trim();

        if (await db.JobCatalogEntries.AnyAsync(e => e.JobCode == jobCode, ct))

            return (false, $"رمز الوظيفة '{jobCode}' مستخدم مسبقاً.", null);

        var entry = new JobCatalogEntry

        {

            JobCode = jobCode,

            TitleAr = payload.TitleAr.Trim(),

            TitleEn = string.IsNullOrWhiteSpace(payload.TitleEn) ? null : payload.TitleEn.Trim(),

            DescriptionAr = string.IsNullOrWhiteSpace(payload.DescriptionAr) ? null : payload.DescriptionAr.Trim(),

            GradeCode = payload.GradeCode.Trim(),

            OccupationalGroup = string.IsNullOrWhiteSpace(payload.OccupationalGroup) ? null : payload.OccupationalGroup.Trim(),

            IsActive = payload.IsActive

        };

        return (true, null, entry);

    }



    private async Task<(bool Success, string? Error, Guid? EntityId, string EntityType, string? BeforeJson, string? AfterJson)>

        ApplyUpdateJobCatalogAsync(EstablishmentChangeRequest request, CancellationToken ct)

    {

        var id = request.TargetEntityId;

        if (id is null || id == Guid.Empty) return (false, "معرف دليل الوظائف مطلوب.", null, nameof(JobCatalogEntry), null, null);



        var entry = await db.JobCatalogEntries.FirstOrDefaultAsync(e => e.Id == id, ct);

        if (entry is null) return (false, "دليل الوظيفة غير موجود.", null, nameof(JobCatalogEntry), null, null);



        var payload = Deserialize<JobCatalogPayload>(request.PayloadJson);

        if (payload is null) return (false, "حمولة غير صالحة.", null, nameof(JobCatalogEntry), null, null);

        var effectiveAt = ResolveEffectiveFrom(request);
        if (await referenceValidator.ValidateGradeCodeAsync(payload.GradeCode, effectiveAt, ct) is { } gradeError)
            return (false, gradeError, null, nameof(JobCatalogEntry), null, null);

        if (await referenceValidator.ValidateJobClassificationAsync(payload.OccupationalGroup, effectiveAt, ct) is { } classError)
            return (false, classError, null, nameof(JobCatalogEntry), null, null);

        var jobCode = string.IsNullOrWhiteSpace(payload.JobCode) ? entry.JobCode : payload.JobCode.Trim();

        if (await db.JobCatalogEntries.AnyAsync(e => e.JobCode == jobCode && e.Id != entry.Id, ct))

            return (false, "رمز الوظيفة مستخدم مسبقاً.", null, nameof(JobCatalogEntry), null, null);



        var before = Serialize(entry);

        entry.JobCode = jobCode;

        entry.TitleAr = payload.TitleAr.Trim();

        entry.TitleEn = string.IsNullOrWhiteSpace(payload.TitleEn) ? null : payload.TitleEn.Trim();

        entry.DescriptionAr = string.IsNullOrWhiteSpace(payload.DescriptionAr) ? null : payload.DescriptionAr.Trim();

        entry.GradeCode = payload.GradeCode.Trim();

        entry.OccupationalGroup = string.IsNullOrWhiteSpace(payload.OccupationalGroup) ? null : payload.OccupationalGroup.Trim();

        entry.IsActive = payload.IsActive;

        await db.SaveChangesAsync(ct);

        await AppendCatalogVersionAsync(entry, request, ct);

        return (true, null, entry.Id, nameof(JobCatalogEntry), before, Serialize(entry));

    }



    private async Task<(bool Success, string? Error, Guid? EntityId, string EntityType, string? BeforeJson, string? AfterJson)>

        ApplyDeactivateJobCatalogAsync(EstablishmentChangeRequest request, CancellationToken ct)

    {

        var id = request.TargetEntityId;

        if (id is null || id == Guid.Empty) return (false, "معرف دليل الوظائف مطلوب.", null, nameof(JobCatalogEntry), null, null);



        var entry = await db.JobCatalogEntries.FirstOrDefaultAsync(e => e.Id == id, ct);

        if (entry is null) return (false, "دليل الوظيفة غير موجود.", null, nameof(JobCatalogEntry), null, null);



        var before = Serialize(entry);

        entry.IsActive = false;

        await db.SaveChangesAsync(ct);

        await AppendCatalogVersionAsync(entry, request, ct);

        return (true, null, entry.Id, nameof(JobCatalogEntry), before, Serialize(entry));

    }



    private async Task<(bool Success, string? Error, Guid? EntityId, string EntityType, string? BeforeJson, string? AfterJson)>

        ApplyCreateFundedPositionAsync(EstablishmentChangeRequest request, CancellationToken ct)

    {

        var payload = Deserialize<FundedPositionCreatePayload>(request.PayloadJson);

        if (payload is null) return (false, "حمولة غير صالحة.", null, nameof(FundedPosition), null, null);



        var catalog = await db.JobCatalogEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == payload.JobCatalogEntryId, ct);

        if (catalog is null) return (false, "دليل الوظائف غير موجود.", null, nameof(FundedPosition), null, null);

        if (!catalog.IsActive) return (false, "دليل الوظائف غير نشط.", null, nameof(FundedPosition), null, null);

        var effectiveAt = ResolveEffectiveFrom(request);
        var gradeCode = catalog.GradeCode;
        if (await referenceValidator.ValidateGradeCodeAsync(gradeCode, effectiveAt, ct) is { } gradeError)
            return (false, gradeError, null, nameof(FundedPosition), null, null);



        if (!await db.OrgUnits.AnyAsync(o => o.Id == payload.OrgUnitId, ct))

            return (false, "الوحدة التنظيمية غير موجودة.", null, nameof(FundedPosition), null, null);



        var draft = new FundedPosition

        {

            GradeCode = gradeCode,

            FTE = payload.FTE,

            BudgetYear = payload.BudgetYear,

            Status = FundedPositionStatus.Vacant,

            OrgUnitId = payload.OrgUnitId,

            JobCatalogEntryId = payload.JobCatalogEntryId

        };



        var created = await fundedPositionCodeService.CreateAsync(draft, ct);

        await AppendPositionVersionAsync(created, request, ct);

        return (true, null, created.Id, nameof(FundedPosition), null, Serialize(created));

    }



    private async Task<(bool Success, string? Error, Guid? EntityId, string EntityType, string? BeforeJson, string? AfterJson)>

        ApplyFreezeFundedPositionAsync(EstablishmentChangeRequest request, CancellationToken ct)

    {

        var payload = Deserialize<FreezeFundedPositionPayload>(request.PayloadJson);

        if (payload is null) return (false, "حمولة غير صالحة.", null, nameof(FundedPosition), null, null);



        var position = await db.FundedPositions.FirstOrDefaultAsync(p => p.Id == payload.FundedPositionId, ct);

        if (position is null) return (false, "المنصب غير موجود.", null, nameof(FundedPosition), null, null);



        var before = Serialize(position);

        // HCM-026 (G-11): an open requisition on the position is cancelled with the freeze, unless an offer is out.
        var blockingRequisitions = await db.RecruitmentRequisitions
            .Where(r => r.FundedPositionId == position.Id && RequisitionWorkflowRules.BlockingStatuses.Contains(r.Status))
            .ToListAsync(ct);
        if (blockingRequisitions.Count > 0)
        {
            var requisitionIds = blockingRequisitions.Select(r => r.Id).ToList();
            var offers = await db.OfferLetters
                .AsNoTracking()
                .Where(o => o.RecruitmentApplication != null
                    && o.RecruitmentApplication.RequisitionId != null
                    && requisitionIds.Contains(o.RecruitmentApplication.RequisitionId.Value))
                .ToListAsync(ct);
            var offerError = EstablishmentRecruitmentCascadeRules.ValidateFreeze(offers);
            if (offerError is not null) return (false, offerError, null, nameof(FundedPosition), before, null);
        }

        var result = establishmentService.FreezePosition(position, payload.FreezeReason.Trim());

        if (!result.Success) return (false, result.Error, null, nameof(FundedPosition), before, null);

        var cancellationReason = EstablishmentRecruitmentCascadeRules.FreezeCancellationReason(payload.FreezeReason);
        foreach (var requisition in blockingRequisitions)
        {
            await RecruitmentRequisitionCascade.StageCancellationAsync(db, requisition, cancellationReason, actorUserId: null, ct);
            // HCM-032 (PLAN-32): aggregate close event on the outbox, same save.
            await Recruitment.Analytics.RecruitmentRequisitionClosedEvent.StageAsync(db, publish, requisition, ct);
        }

        await db.SaveChangesAsync(ct);

        await AppendPositionVersionAsync(position, request, ct);

        return (true, null, position.Id, nameof(FundedPosition), before, Serialize(position));

    }



    private async Task<(bool Success, string? Error, Guid? EntityId, string EntityType, string? BeforeJson, string? AfterJson)>

        ApplyUnfreezeFundedPositionAsync(EstablishmentChangeRequest request, CancellationToken ct)

    {

        var payload = Deserialize<UnfreezeFundedPositionPayload>(request.PayloadJson);

        if (payload is null) return (false, "حمولة غير صالحة.", null, nameof(FundedPosition), null, null);



        var position = await db.FundedPositions.FirstOrDefaultAsync(p => p.Id == payload.FundedPositionId, ct);

        if (position is null) return (false, "المنصب غير موجود.", null, nameof(FundedPosition), null, null);



        var before = Serialize(position);

        var result = establishmentService.UnfreezePosition(position);

        if (!result.Success) return (false, result.Error, null, nameof(FundedPosition), before, null);



        await db.SaveChangesAsync(ct);

        await AppendPositionVersionAsync(position, request, ct);

        return (true, null, position.Id, nameof(FundedPosition), before, Serialize(position));

    }



    private static T? Deserialize<T>(string json)

    {

        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }

        catch { return default; }

    }



    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

}


