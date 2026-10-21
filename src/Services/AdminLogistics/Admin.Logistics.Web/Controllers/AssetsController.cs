using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using Admin.Logistics.Infrastructure.Abstractions;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Infrastructure.Integration;
using Admin.Logistics.Web.Helpers;
using Admin.Logistics.Web.Services;
using Admin.Logistics.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Integration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.ServiceDefaults.Mvc;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Web.Controllers;

public class AssetsController(
    AdminLogisticsDbContext db,
    IFileReferenceClient fileRefs,
    IEmployeeLookup employeeLookup,
    WorkflowTriggerService workflowTrigger,
    IPolicyEnforceClient policyClient,
    ICodeGenerationClient codeClient,
    IAssetClassifier assetClassifier) : Controller
{
    public async Task<IActionResult> Index(
        string? q,
        AssetStatus? status,
        string? category,
        string? brand,
        Guid? holderEmployeeId,
        bool unassignedOnly = false,
        bool assignedOnly = false,
        bool privilegedOnly = false,
        string? originKind = null,
        DateTime? purchaseFrom = null,
        DateTime? purchaseTo = null,
        decimal? valueMin = null,
        decimal? valueMax = null,
        bool activeOnly = false,
        bool showCancelled = false,
        int page = 1,
        int pageSize = SearchFilterBase.DefaultPageSize)
    {
        var filter = new AssetIndexFilter
        {
            Q = q,
            Status = status,
            Category = category,
            Brand = brand,
            HolderEmployeeId = holderEmployeeId,
            UnassignedOnly = unassignedOnly,
            AssignedOnly = assignedOnly,
            PrivilegedOnly = privilegedOnly,
            OriginKind = originKind,
            PurchaseFrom = purchaseFrom,
            PurchaseTo = purchaseTo,
            ValueMin = valueMin,
            ValueMax = valueMax,
            ActiveOnly = activeOnly,
            ShowCancelled = showCancelled,
            Page = page,
            PageSize = pageSize
        };
        var (normalizedPage, normalizedPageSize) = filter.NormalizePaging();

        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots, HttpContext.RequestAborted);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;

        var categoryOptions = await db.Assets.AsNoTracking()
            .Where(a => a.Category != null && a.Category != "")
            .Select(a => a.Category!)
            .Distinct()
            .OrderBy(c => c)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .ToListAsync();
        ViewBag.Categories = new SelectList(categoryOptions, category);

        var brandOptions = await db.Assets.AsNoTracking()
            .Where(a => a.Brand != null && a.Brand != "")
            .Select(a => a.Brand!)
            .Distinct()
            .OrderBy(b => b)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .ToListAsync();
        ViewBag.Brands = new SelectList(brandOptions, brand);

        var assetQuery = filter.Apply(db.Assets.AsNoTracking());
        if (scope.MustFilter)
        {
            var inScopeAssetIds = await (
                from a in db.AssetAssignments.AsNoTracking()
                join e in db.EmployeeOrgUnitSnapshots.AsNoTracking() on a.EmployeeId equals e.EmployeeId
                where a.ReturnedAt == null
                      && a.Status == AssetAssignmentStatus.Active
                      && e.PrimaryOrgUnitId.HasValue
                      && scope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value)
                select a.AssetId).Distinct().ToListAsync(HttpContext.RequestAborted);
            assetQuery = assetQuery.Where(a => inScopeAssetIds.Contains(a.Id));
        }

        var pagedAssets = await assetQuery
            .OrderByDescending(x => x.CreatedAt)
            .ToPaginatedListAsync(normalizedPage, normalizedPageSize, HttpContext.RequestAborted);

        var assetIds = pagedAssets.Items.Select(a => a.Id).ToList();
        var openHolders = assetIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.AssetAssignments.AsNoTracking()
                .Where(a => assetIds.Contains(a.AssetId) && a.ReturnedAt == null && a.Status == AssetAssignmentStatus.Active)
                .Select(a => new { a.AssetId, a.EmployeeId })
                .ToDictionaryAsync(x => x.AssetId, x => x.EmployeeId);

        var employeeNames = await employeeLookup.GetEmployeeDisplayNamesAsync(openHolders.Values, HttpContext.RequestAborted);

        var listItems = pagedAssets.Items.Select(a => new AssetListItemViewModel
        {
            Id = a.Id,
            TagNumber = a.TagNumber,
            Name = a.Name,
            Category = a.Category,
            Status = a.Status,
            IsPrivileged = a.IsPrivileged,
            IsCancelled = a.IsCancelled,
            OpenHolderEmployeeId = openHolders.GetValueOrDefault(a.Id),
            OpenHolderEmployeeName = openHolders.TryGetValue(a.Id, out var empId)
                && employeeNames.TryGetValue(empId, out var n)
                && HrLookupDisplayHelper.IsResolvedEmployeeName(n)
                ? n
                : null,
            OriginReceiptRecordId = a.OriginReceiptRecordId,
            IsLegacyEntry = a.IsLegacyEntry,
            OriginSummary = a.IsLegacyEntry ? "قديم/هبة" : a.OriginReceiptRecordId.HasValue ? "استلام" : "—"
        }).ToList();

        var pagedItems = new PaginatedList<AssetListItemViewModel>(
            listItems, pagedAssets.TotalCount, pagedAssets.PageIndex, pagedAssets.PageSize);

        string? filterHolderEmployeeName = null;
        if (holderEmployeeId is Guid hid && hid != Guid.Empty)
            filterHolderEmployeeName = await employeeLookup.GetEmployeeDisplayNameAsync(hid, HttpContext.RequestAborted);

        return View(new AssetIndexViewModel
        {
            Items = pagedItems,
            Q = q,
            Status = status,
            Category = category,
            Brand = brand,
            HolderEmployeeId = holderEmployeeId is Guid he && he != Guid.Empty ? he : null,
            FilterHolderEmployeeName = filterHolderEmployeeName,
            UnassignedOnly = unassignedOnly,
            AssignedOnly = assignedOnly,
            PrivilegedOnly = privilegedOnly,
            OriginKind = originKind,
            PurchaseFrom = purchaseFrom,
            PurchaseTo = purchaseTo,
            ValueMin = valueMin,
            ValueMax = valueMax,
            ActiveOnly = activeOnly,
            ShowCancelled = showCancelled
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        if (await this.ForbidIfAssetOutOfScopeAsync(db, id, HttpContext.RequestAborted) is { } denied)
            return denied;

        var asset = await db.Assets
            .Include(a => a.Assignments)
            .Include(a => a.LossReports)
            .Include(a => a.MaintenanceRecords)
            .Include(a => a.OriginReceiptRecord!)
                .ThenInclude(r => r!.SupplyOrder)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (asset is null) return NotFound();

        var employeeIds = asset.Assignments.Select(a => a.EmployeeId).Distinct();
        ViewBag.EmployeeNames = await employeeLookup.GetEmployeeDisplayNamesAsync(employeeIds, HttpContext.RequestAborted);
        AssetDetailsFormDraftStore.ApplyToViewBag(ViewData, TempData, id);

        return View(asset);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.ReceiptRecords = new SelectList(
            await db.ReceiptRecords.AsNoTracking()
                .OrderByDescending(r => r.ReceivedAt)
                .Take(SearchFilterBase.LookupMaxPageSize)
                .Select(r => new { r.Id, Label = $"{r.ReceivedAt:yyyy/MM/dd} — {r.ItemsReceived}" })
                .ToListAsync(),
            "Id", "Label");
        return View();
    }

    public IActionResult CreateLegacy() => View(new AssetLegacyCreateFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLegacy(AssetLegacyCreateFormViewModel model)
    {
        var entity = new Asset
        {
            IsLegacyEntry = true,
            OriginReceiptRecordId = null,
            OriginSupplyOrderLineId = null,
            Name = model.Name,
            Category = model.Category,
            Brand = model.Brand,
            Model = model.Model,
            PurchaseDate = model.PurchaseDate,
            Value = model.Value,
            Status = model.Status,
            LegacyEntryReason = model.LegacyEntryReason
        };

        if (!AssetOriginRules.IsValidOrigin(entity))
            ModelState.AddModelError(nameof(model.LegacyEntryReason),
                $"أصل قديم/هبة يتطلب سبباً ({AssetOriginRules.MinLegacyReasonLength} أحرف على الأقل) بدون سجل استلام");

        if (!ModelState.IsValid)
        {
            ViewData["ShowValidationDialog"] = true;
            return View(model);
        }

        await assetClassifier.ApplyClassificationAsync(entity, HttpContext.RequestAborted);

        var (tagOk, tag, tagError) = await GenerateAssetTagAsync(entity);
        if (!tagOk)
        {
            ModelState.AddModelError(string.Empty, tagError!);
            ViewData["ShowValidationDialog"] = true;
            return View(model);
        }

        entity.TagNumber = tag!;

        db.Assets.Add(entity);
        var outcome = await HrSaveChanges.TryAsync(db, ModelState, HttpContext.RequestAborted);
        if (outcome != SaveChangesOutcome.Success)
        {
            ViewData["ShowValidationDialog"] = true;
            return View(model);
        }
        TempData["Success"] = "تمت إضافة الأصل (إدخال استثنائي)";
        return RedirectToAction(nameof(Details), new { id = entity.Id });
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        if (await this.ForbidIfAssetOutOfScopeAsync(db, id, HttpContext.RequestAborted) is { } denied)
            return denied;

        var e = await db.Assets
            .Include(a => a.OriginReceiptRecord)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (e is null) return NotFound();
        var model = new AssetEditFormViewModel
        {
            Id = e.Id,
            TagNumber = e.TagNumber,
            Name = e.Name,
            Category = e.Category,
            Brand = e.Brand,
            Model = e.Model,
            PurchaseDate = e.PurchaseDate,
            Value = e.Value,
            Status = e.Status,
            OriginReceiptRecordId = e.OriginReceiptRecordId,
            IsLegacyEntry = e.IsLegacyEntry,
            LegacyEntryReason = e.LegacyEntryReason,
            OriginReceiptReceivedAt = e.OriginReceiptRecord?.ReceivedAt,
            OriginReceiptItemsReceived = e.OriginReceiptRecord?.ItemsReceived,
            IsPrivileged = e.IsPrivileged
        };
        if (!e.OriginReceiptRecordId.HasValue && !e.IsLegacyEntry)
            await LoadReceiptSelectListAsync(model.OriginReceiptRecordId);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, AssetEditFormViewModel model)
    {
        if (id != model.Id) return NotFound();
        if (await this.ForbidIfAssetOutOfScopeAsync(db, id, HttpContext.RequestAborted) is { } denied)
            return denied;

        var e = await db.Assets.FindAsync(id);
        if (e is null) return NotFound();

        var originProbe = new Asset
        {
            OriginReceiptRecordId = model.OriginReceiptRecordId,
            IsLegacyEntry = model.IsLegacyEntry,
            LegacyEntryReason = model.LegacyEntryReason
        };
        if (!AssetOriginRules.CanChangeOrigin(e, originProbe))
        {
            ModelState.AddModelError(string.Empty, "لا يمكن تغيير مصدر التوريد بعد التعيين");
            model.OriginReceiptRecordId = e.OriginReceiptRecordId;
            model.IsLegacyEntry = e.IsLegacyEntry;
            model.LegacyEntryReason = e.LegacyEntryReason;
        }

        if (!ModelState.IsValid)
        {
            ViewData["ShowValidationDialog"] = true;
            model.TagNumber = e.TagNumber;
            if (!e.OriginReceiptRecordId.HasValue && !e.IsLegacyEntry)
                await LoadReceiptSelectListAsync(model.OriginReceiptRecordId);
            return View(model);
        }

        // TagNumber immutable after issuance (COD-001)
        e.Name = model.Name;
        e.Category = model.Category;
        e.Brand = model.Brand;
        e.Model = model.Model;
        e.PurchaseDate = model.PurchaseDate;
        e.Value = model.Value;
        e.Status = model.Status;

        if (!e.OriginReceiptRecordId.HasValue && !e.IsLegacyEntry)
            e.OriginReceiptRecordId = model.OriginReceiptRecordId;

        await assetClassifier.ApplyClassificationAsync(e, HttpContext.RequestAborted);

        var outcome = await HrSaveChanges.TryAsync(db, ModelState, HttpContext.RequestAborted);
        if (outcome != SaveChangesOutcome.Success)
        {
            ViewData["ShowValidationDialog"] = true;
            model.TagNumber = e.TagNumber;
            if (!e.OriginReceiptRecordId.HasValue && !e.IsLegacyEntry)
                await LoadReceiptSelectListAsync(model.OriginReceiptRecordId);
            return View(model);
        }
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignToEmployee(
        Guid id,
        Guid employeeId,
        DateTime? dueBackDate,
        AssetHandoverCondition? handoverCondition,
        string? handoverConditionNotes,
        string? evidenceRef,
        Guid? handoverFileId,
        string? handoverUploadInProgress)
    {
        var draft = new AssignToEmployeeDraft
        {
            EmployeeId = employeeId,
            DueBackDate = dueBackDate,
            HandoverCondition = handoverCondition,
            HandoverConditionNotes = handoverConditionNotes,
            EvidenceRef = evidenceRef,
            HandoverFileId = handoverFileId
        };

        IActionResult Fail(string message)
        {
            AssetDetailsFormDraftStore.SaveAssignDraft(TempData, id, draft);
            AssetDetailsFormDraftStore.SetFormError(TempData, id, AssetDetailsFormKeys.Assign, message);
            return RedirectToAction(nameof(Details), new { id });
        }

        var asset = await db.Assets.FindAsync(id);
        if (asset is null) return NotFound();

        if (await this.ForbidIfAssetOutOfScopeAsync(db, id, HttpContext.RequestAborted) is { } assetDenied)
            return assetDenied;
        if (await this.ForbidIfEmployeeOutOfScopeForCustodyAsync(db, employeeId, HttpContext.RequestAborted) is { } empDenied)
            return empDenied;

        if (employeeId == Guid.Empty)
            return Fail(AssetDetailsFormValidation.MissingEmployeeMessage);

        if (AssetDetailsFormValidation.IndicatesUploadInProgress(handoverUploadInProgress))
            return Fail(AssetDetailsFormValidation.UploadInProgressMessage);

        var open = await db.AssetAssignments.AsNoTracking()
            .Where(a => a.AssetId == id && a.ReturnedAt == null)
            .ToListAsync();
        if (!AssetAssignmentRules.CanAssign(asset, open))
            return Fail("لا يمكن التعيين: يوجد حائز مفتوح أو الأصل غير قابل للتسليم");

        var assignmentId = Guid.NewGuid();
        var contextJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            assetId = id,
            employeeId,
            tagNumber = asset.TagNumber,
            isPrivileged = asset.IsPrivileged,
            value = asset.Value
        });
        var policyResult = await policyClient.EnforceAsync(
            AssetPolicyFactsBuilder.ForAssign(asset, asset.TenantId, assignmentId, contextJson),
            HttpContext.RequestAborted);

        if (!policyResult.Allowed)
            return Fail(policyResult.Decision.DenyReason ?? "مرفوض بسياسة العهدة");

        var needsEvidence = policyResult.Decision.Effects.Contains(PolicyEffect.RequireEvidence);
        if (needsEvidence && !AssetHandoverRules.HasProof(evidenceRef, handoverFileId))
            return Fail(AssetDetailsFormValidation.MissingProofMessage);

        var requiresApproval = policyResult.WorkflowInstanceId.HasValue
                               || policyResult.Decision.Effects.Contains(PolicyEffect.TriggerWorkflow);
        var assignment = new AssetAssignment
        {
            Id = assignmentId,
            AssetId = id,
            EmployeeId = employeeId,
            AssignedAt = DateTime.UtcNow,
            DueBackDate = dueBackDate,
            HandoverCondition = handoverCondition ?? AssetHandoverCondition.Good,
            HandoverConditionNotes = string.IsNullOrWhiteSpace(handoverConditionNotes) ? null : handoverConditionNotes.Trim(),
            EvidenceRef = evidenceRef?.Trim(),
            HandoverFileId = handoverFileId,
            Status = requiresApproval ? AssetAssignmentStatus.PendingApproval : AssetAssignmentStatus.Active
        };

        if (handoverFileId.HasValue)
        {
            try
            {
                await fileRefs.RegisterReferenceAsync(
                    handoverFileId.Value,
                    FileReferenceClientExtensions.BuildHolderKey(
                        HrFileServices.AdminLogistics, nameof(AssetAssignment), assignmentId, nameof(AssetAssignment.HandoverFileId)));
            }
            catch (Exception ex)
            {
                return Fail($"مرفق مرفوض (FILE-002): {ex.Message}");
            }
        }

        db.AssetAssignments.Add(assignment);

        if (requiresApproval)
        {
            var workflowId = policyResult.WorkflowInstanceId
                ?? await workflowTrigger.StartAssetAssignmentWorkflowAsync(
                    assignmentId, id, employeeId, asset.TagNumber, asset.IsPrivileged, asset.Value, HttpContext.RequestAborted);
            assignment.WorkflowInstanceId = workflowId;
            TempData["Success"] = workflowId.HasValue
                ? "تم إرسال طلب التسليم للموافقة"
                : "تم إرسال طلب التسليم (تعذّر بدء سير الموافقة — راجع الإعدادات)";
        }
        else
        {
            asset.Status = AssetStatus.InUse;
            TempData["Success"] = "تم التسليم للموظف";
        }

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnAssignment(
        Guid assignmentId,
        Guid assetId,
        string? evidenceRef,
        Guid? returnFileId,
        string? returnUploadInProgress)
    {
        var draft = new ReturnAssignmentDraft
        {
            EvidenceRef = evidenceRef,
            ReturnFileId = returnFileId
        };

        IActionResult Fail(string message)
        {
            AssetDetailsFormDraftStore.SaveReturnDraft(TempData, assetId, draft);
            AssetDetailsFormDraftStore.SetFormError(TempData, assetId, AssetDetailsFormKeys.Return, message);
            return RedirectToAction(nameof(Details), new { id = assetId });
        }

        var assignment = await db.AssetAssignments.FindAsync(assignmentId);
        if (assignment is null) return NotFound();
        if (await this.ForbidIfAssetOutOfScopeAsync(db, assetId, HttpContext.RequestAborted) is { } denied)
            return denied;

        if (assignment.Status != AssetAssignmentStatus.Active)
            return Fail("لا يمكن استرداد عهدة غير نشطة");

        if (AssetDetailsFormValidation.IndicatesUploadInProgress(returnUploadInProgress))
            return Fail(AssetDetailsFormValidation.UploadInProgressMessage);

        var asset = await db.Assets.FindAsync(assetId);
        if (asset is not null && asset.IsPrivileged && !AssetHandoverRules.HasProof(evidenceRef, returnFileId))
            return Fail(AssetDetailsFormValidation.MissingReturnProofMessage);

        if (returnFileId.HasValue)
        {
            try
            {
                await fileRefs.RegisterReferenceAsync(
                    returnFileId.Value,
                    FileReferenceClientExtensions.BuildHolderKey(
                        HrFileServices.AdminLogistics, nameof(AssetAssignment), assignmentId, nameof(AssetAssignment.ReturnFileId)));
                assignment.ReturnFileId = returnFileId;
            }
            catch (Exception ex)
            {
                return Fail($"مرفق مرفوض (FILE-002): {ex.Message}");
            }
        }

        assignment.ReturnedAt = DateTime.UtcNow;
        assignment.Status = AssetAssignmentStatus.Returned;
        if (!string.IsNullOrWhiteSpace(evidenceRef))
            assignment.EvidenceRef = evidenceRef.Trim();
        if (asset is not null) asset.Status = AssetStatus.InStorage;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم استرداد العهدة";
        return RedirectToAction(nameof(Details), new { id = assetId });
    }

    public async Task<IActionResult> HandoverReceiptPdf(Guid assignmentId)
    {
        var assignment = await db.AssetAssignments.Include(a => a.Asset).FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment?.Asset is null) return NotFound();
        if (await this.ForbidIfAssetOutOfScopeAsync(db, assignment.AssetId, HttpContext.RequestAborted) is { } denied)
            return denied;

        var employeeName = await employeeLookup.GetEmployeeDisplayNameAsync(assignment.EmployeeId, HttpContext.RequestAborted);
        var bytes = AssetHandoverPdfService.Generate(assignment.Asset, assignment, employeeName);
        return File(bytes, "application/pdf", $"handover-{assignment.Asset.TagNumber}-{assignment.AssignedAt:yyyyMMdd}.pdf");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkLost(
        Guid id,
        AssetLossReason reason,
        AssetLossContext context,
        string? notes,
        string? evidenceRef,
        Guid? lossFileId,
        string? lossUploadInProgress)
    {
        var draft = new ReportLossDraft
        {
            Reason = reason,
            Context = context,
            Notes = notes,
            EvidenceRef = evidenceRef,
            LossFileId = lossFileId
        };

        IActionResult Fail(string message)
        {
            AssetDetailsFormDraftStore.SaveLossDraft(TempData, id, draft);
            AssetDetailsFormDraftStore.SetFormError(TempData, id, AssetDetailsFormKeys.Loss, message);
            return RedirectToAction(nameof(Details), new { id });
        }

        var asset = await db.Assets
            .Include(a => a.Assignments)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (asset is null) return NotFound();
        if (await this.ForbidIfAssetOutOfScopeAsync(db, id, HttpContext.RequestAborted) is { } denied)
            return denied;

        if (AssetDetailsFormValidation.IndicatesUploadInProgress(lossUploadInProgress))
            return Fail(AssetDetailsFormValidation.UploadInProgressMessage);

        var pendingReport = await db.AssetLossReports.AsNoTracking()
            .FirstOrDefaultAsync(r => r.AssetId == id && r.Status == AssetLossReportStatus.PendingApproval);
        if (!AssetLostRules.CanRequestLoss(asset, pendingReport))
        {
            return Fail(pendingReport is not null
                ? "يوجد طلب فقدان بانتظار الموافقة"
                : "الأصل غير قابل لوسم الفقدان");
        }

        if (!AssetLostRules.HasLossProof(asset, evidenceRef, lossFileId))
            return Fail(AssetDetailsFormValidation.MissingLossProofMessage);

        var reportId = Guid.NewGuid();
        var report = new AssetLossReport
        {
            Id = reportId,
            AssetId = id,
            Reason = reason,
            Context = context,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            EvidenceRef = evidenceRef?.Trim(),
            LossFileId = lossFileId,
            Status = AssetLossReportStatus.PendingApproval
        };

        if (lossFileId.HasValue)
        {
            try
            {
                await fileRefs.RegisterReferenceAsync(
                    lossFileId.Value,
                    FileReferenceClientExtensions.BuildHolderKey(
                        HrFileServices.AdminLogistics, nameof(AssetLossReport), reportId, nameof(AssetLossReport.LossFileId)));
            }
            catch (Exception ex)
            {
                return Fail($"مرفق مرفوض (FILE-002): {ex.Message}");
            }
        }

        db.AssetLossReports.Add(report);

        var openForCapture = await db.AssetAssignments
            .Where(a => a.AssetId == id && a.ReturnedAt == null)
            .ToListAsync();
        AssetLossStateCapture.CaptureBefore(asset, openForCapture, report);

        if (AssetLostRules.RequiresLossApproval(asset))
        {
            var workflowId = await workflowTrigger.StartAssetLossWorkflowAsync(
                reportId, id, asset.TagNumber, asset.IsPrivileged, asset.Value, reason, HttpContext.RequestAborted);
            report.WorkflowInstanceId = workflowId;
            await db.SaveChangesAsync();
            TempData["Success"] = workflowId.HasValue
                ? "تم إرسال طلب الفقدان للموافقة"
                : "تم تسجيل طلب الفقدان (تعذّر بدء سير الموافقة — راجع الإعدادات)";
            return RedirectToAction(nameof(Details), new { id });
        }

        var open = await db.AssetAssignments
            .Where(a => a.AssetId == id && a.ReturnedAt == null)
            .ToListAsync();
        var holderName = await ResolveHolderDisplayNameAsync(open);
        AssetLostRules.ApplyLost(asset, open, report, holderName);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم وسم الأصل مفقوداً";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecoverAsset(Guid id, string recoveryNotes)
    {
        if (await this.ForbidIfAssetOutOfScopeAsync(db, id, HttpContext.RequestAborted) is { } denied)
            return denied;

        var asset = await db.Assets.FindAsync(id);
        if (asset is null) return NotFound();
        if (!AssetLostRules.CanRecover(asset))
        {
            AssetDetailsFormDraftStore.SetDialogError(TempData, "لا يمكن استرداد هذا الأصل");
            return RedirectToAction(nameof(Details), new { id });
        }

        if (string.IsNullOrWhiteSpace(recoveryNotes) || recoveryNotes.Trim().Length < 5)
        {
            AssetDetailsFormDraftStore.SetDialogError(TempData, "ملاحظات الاسترداد مطلوبة (5 أحرف على الأقل)");
            return RedirectToAction(nameof(Details), new { id });
        }

        var confirmedReport = await db.AssetLossReports
            .Where(r => r.AssetId == id && r.Status == AssetLossReportStatus.Confirmed)
            .OrderByDescending(r => r.ConfirmedLostAt)
            .FirstOrDefaultAsync();
        if (confirmedReport is null)
        {
            AssetDetailsFormDraftStore.SetDialogError(TempData, "لا يوجد تقرير فقدان مؤكد للاسترداد");
            return RedirectToAction(nameof(Details), new { id });
        }

        AssetLostRules.Recover(asset, confirmedReport, recoveryNotes);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إيجاد الأصل وإعادته إلى المخزن";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelAsset(Guid id, string? reason)
    {
        if (await this.ForbidIfAssetOutOfScopeAsync(db, id, HttpContext.RequestAborted) is { } denied)
            return denied;

        var asset = await db.Assets
            .Include(a => a.Assignments)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (asset is null) return NotFound();
        if (asset.IsCancelled)
        {
            AssetDetailsFormDraftStore.SetDialogError(TempData, "الأصل ملغى مسبقاً");
            return RedirectToAction(nameof(Details), new { id });
        }

        if (AssetAssignmentRules.HasOpenAssignment(asset.Assignments, id))
        {
            AssetDetailsFormDraftStore.SetDialogError(TempData, "لا يمكن الإلغاء: يوجد عهدة مفتوحة — استردادها أولاً");
            return RedirectToAction(nameof(Details), new { id });
        }

        AssetAssignmentRules.SoftCancel(asset, reason);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء الأصل";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFromReceipt(Guid receiptId, Guid? supplyOrderLineId)
    {
        var receipt = await db.ReceiptRecords
            .Include(r => r.SupplyOrder)
                .ThenInclude(o => o!.Lines)
                    .ThenInclude(l => l.InventoryItem)
            .FirstOrDefaultAsync(r => r.Id == receiptId);
        if (receipt is null) return NotFound();

        SupplyOrderLine? line = null;
        if (supplyOrderLineId.HasValue)
        {
            line = receipt.SupplyOrder?.Lines.FirstOrDefault(l => l.Id == supplyOrderLineId.Value);
            if (line is null)
            {
                AdminLogisticsFlash.Error(TempData, "بند طلب التجهيز غير صالح");
                return RedirectToAction("Details", "ReceiptRecords", new { id = receiptId });
            }

            var created = await db.Assets.CountAsync(a => a.OriginSupplyOrderLineId == line.Id);
            if (SupplyReceiptRules.RemainingAssetSlots(line, created) <= 0)
            {
                AdminLogisticsFlash.Error(TempData, "تم إنشاء جميع الأصول لهذا البند");
                return RedirectToAction("Details", "ReceiptRecords", new { id = receiptId });
            }
        }

        var label = line?.InventoryItem?.Name ?? line?.Sku ?? receipt.ItemsReceived;
        var draftAsset = new Asset { Value = line?.UnitCost, Category = line?.IsCapitalizable == true ? "أصول ثابتة" : null };
        await assetClassifier.ApplyClassificationAsync(draftAsset, HttpContext.RequestAborted);
        var (tagOk, tag, tagError) = await GenerateAssetTagAsync(draftAsset);
        if (!tagOk)
        {
            AdminLogisticsFlash.Error(TempData, tagError ?? "تعذّر توليد رقم العهدة");
            return RedirectToAction("Details", "ReceiptRecords", new { id = receiptId });
        }

        var asset = new Asset
        {
            Status = AssetStatus.InStorage,
            OriginReceiptRecordId = receiptId,
            OriginSupplyOrderLineId = supplyOrderLineId,
            Name = label ?? $"أصل من استلام {receipt.ReceivedAt:yyyyMMdd}",
            Category = line?.IsCapitalizable == true ? "أصول ثابتة" : null,
            Value = line?.UnitCost,
            PurchaseDate = receipt.ReceivedAt,
            TagNumber = tag!,
            IsPrivileged = draftAsset.IsPrivileged
        };

        if (!AssetOriginRules.IsValidOrigin(asset))
        {
            AdminLogisticsFlash.Error(TempData, "تعذّر إنشاء الأصل: مصدر غير صالح");
            return RedirectToAction("Details", "ReceiptRecords", new { id = receiptId });
        }

        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إنشاء أصل من سجل الاستلام — أكمل البيانات";
        return RedirectToAction(nameof(Edit), new { id = asset.Id });
    }

    private async Task PrepareAssetEditViewAsync(Asset entity, string? persistedTagNumber = null)
    {
        if (persistedTagNumber is not null)
            entity.TagNumber = persistedTagNumber;

        if (entity.OriginReceiptRecordId is Guid receiptId)
        {
            entity.OriginReceiptRecord = await db.ReceiptRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == receiptId);
        }

        if (!entity.OriginReceiptRecordId.HasValue && !entity.IsLegacyEntry)
            await LoadReceiptSelectListAsync(entity.OriginReceiptRecordId);
    }

    private async Task LoadReceiptSelectListAsync(Guid? selected = null)
    {
        ViewBag.ReceiptRecords = new SelectList(
            await db.ReceiptRecords.AsNoTracking()
                .OrderByDescending(r => r.ReceivedAt)
                .Take(SearchFilterBase.LookupMaxPageSize)
                .Select(r => new { r.Id, Label = $"{r.ReceivedAt:yyyy/MM/dd} — {r.ItemsReceived}" })
                .ToListAsync(),
            "Id", "Label", selected);
    }

    private async Task<(bool Ok, string? Code, string? Error)> GenerateAssetTagAsync(Asset asset)
    {
        string? policyKey = null;
        var policyResult = await policyClient.EnforceAsync(
            AssetPolicyFactsBuilder.ForCreate(asset, asset.TenantId),
            HttpContext.RequestAborted);
        if (!policyResult.Allowed)
            return (false, null, policyResult.Decision.DenyReason ?? "مرفوض بسياسة العهدة");
        policyKey = policyResult.Decision.GetCodePolicyKey();

        var (ok, code, error) = await CodeGenerationGateway.GenerateOrErrorAsync(
            codeClient,
            CodeGenerationRequests.AssetTag(policyKey));
        return (ok, code, error);
    }

    private async Task<string?> ResolveHolderDisplayNameAsync(IReadOnlyList<AssetAssignment> openAssignments)
    {
        var holderId = openAssignments.FirstOrDefault()?.EmployeeId;
        if (holderId is not Guid hid || hid == Guid.Empty)
            return null;

        var names = await employeeLookup.GetEmployeeDisplayNamesAsync([hid], HttpContext.RequestAborted);
        return names.TryGetValue(hid, out var name) ? name : null;
    }
}
