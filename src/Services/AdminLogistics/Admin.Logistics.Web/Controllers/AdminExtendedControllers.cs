using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using Admin.Logistics.Infrastructure.Abstractions;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Infrastructure.Services;
using Admin.Logistics.Web.Helpers;
using Admin.Logistics.Web.Services;
using Admin.Logistics.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.ViewModels;
using HRManagement.ServiceDefaults;
using System.Security.Claims;

namespace Admin.Logistics.Web.Controllers;

public class SupplyOrdersController(AdminLogisticsDbContext db, IEmployeeLookup employeeLookup) : Controller
{
    public async Task<IActionResult> Index(
        string? q,
        SupplyOrderStatus? status,
        Guid? requestedBy,
        Guid? orgUnitId,
        DateTime? orderedFrom,
        DateTime? orderedTo,
        DateTime? fulfilledFrom,
        DateTime? fulfilledTo,
        bool activeOnly = false,
        bool awaitingReceiptOnly = false,
        int page = 1,
        int pageSize = SearchFilterBase.DefaultPageSize)
    {
        var filter = new SupplyOrderIndexFilter
        {
            Q = q,
            Status = status,
            RequestedBy = requestedBy,
            OrgUnitId = orgUnitId,
            OrderedFrom = orderedFrom,
            OrderedTo = orderedTo,
            FulfilledFrom = fulfilledFrom,
            FulfilledTo = fulfilledTo,
            ActiveOnly = activeOnly,
            AwaitingReceiptOnly = awaitingReceiptOnly,
            Page = page,
            PageSize = pageSize
        };
        var (normalizedPage, normalizedPageSize) = filter.NormalizePaging();

        var paged = await filter.Apply(db.SupplyOrders.AsNoTracking())
            .OrderByDescending(x => x.OrderedAt)
            .ToPaginatedListAsync(normalizedPage, normalizedPageSize, HttpContext.RequestAborted);

        var employeeIds = paged.Items
            .Where(i => i.RequestedBy.HasValue)
            .Select(i => i.RequestedBy!.Value)
            .Distinct()
            .ToList();
        ViewBag.EmployeeNames = await employeeLookup.GetEmployeeDisplayNamesAsync(employeeIds, HttpContext.RequestAborted);

        string? filterRequesterName = null;
        if (requestedBy is Guid reqId && reqId != Guid.Empty)
            filterRequesterName = await employeeLookup.GetEmployeeDisplayNameAsync(reqId, HttpContext.RequestAborted);

        return View(new SupplyOrderIndexViewModel
        {
            Items = paged,
            Q = q,
            Status = status,
            RequestedBy = requestedBy is Guid rb && rb != Guid.Empty ? rb : null,
            FilterRequesterName = filterRequesterName,
            OrgUnitId = orgUnitId is Guid ou && ou != Guid.Empty ? ou : null,
            OrderedFrom = orderedFrom,
            OrderedTo = orderedTo,
            FulfilledFrom = fulfilledFrom,
            FulfilledTo = fulfilledTo,
            ActiveOnly = activeOnly,
            AwaitingReceiptOnly = awaitingReceiptOnly
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.SupplyOrders
            .Include(o => o.Lines)
            .ThenInclude(l => l.InventoryItem)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();

        var receipts = await db.ReceiptRecords.AsNoTracking()
            .Where(r => r.SupplyOrderId == id)
            .OrderByDescending(r => r.ReceivedAt)
            .ToListAsync();
        ViewBag.ReceiptRecords = receipts;
        ViewBag.ReceiptCount = receipts.Count;
        ViewBag.IsFullyReceived = SupplyReceiptRules.IsFullyReceived(e);
        ViewBag.CanClose = SupplyOrderWorkflowRules.CanClose(e, receipts.Count);
        ViewBag.CanEditContent = SupplyOrderWorkflowRules.CanEditContent(e.Status);

        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid? currentUserId = Guid.TryParse(rawUserId, out var uid) ? uid : null;
        ViewBag.CanApprove = SupplyOrderWorkflowRules.CanApprove(e, currentUserId);
        if (e.Status == SupplyOrderStatus.Pending
            && SupplyOrderWorkflowRules.CanApprove(e.Status)
            && !SupplyOrderWorkflowRules.ApproverIsDistinctFromRequester(e.RequestedBy, currentUserId))
        {
            ViewBag.ApproveBlockedReason = "لا يمكنك الموافقة على طلبك — يلزم مستخدم آخر";
        }

        var employeeIds = new List<Guid>();
        if (e.RequestedBy.HasValue) employeeIds.Add(e.RequestedBy.Value);
        ViewBag.EmployeeNames = await employeeLookup.GetEmployeeDisplayNamesAsync(employeeIds, HttpContext.RequestAborted);
        ViewBag.OrgUnitId = e.OrgUnitId;

        return View(e);
    }

    public IActionResult Create() => View(new SupplyOrderFormViewModel
    {
        OrderedAt = DateTimeLocalInput.TruncateToMinute(DateTime.UtcNow)
    });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SupplyOrderFormViewModel model)
    {
        model.OrderedAt = DateTimeLocalInput.TruncateToMinute(model.OrderedAt);
        if (!ModelState.IsValid) return View(model);
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var entity = new SupplyOrder
        {
            OrgUnitId = model.OrgUnitId,
            Items = model.Items,
            OrderedAt = model.OrderedAt,
            Status = SupplyOrderStatus.Pending,
            FulfilledAt = null
        };
        if (Guid.TryParse(rawUserId, out var requesterId))
            entity.RequestedBy = requesterId;
        db.SupplyOrders.Add(entity);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل طلب التجهيز";
        return RedirectToAction(nameof(Details), new { id = entity.Id });
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.SupplyOrders.FindAsync(id);
        if (e is null) return NotFound();
        if (!SupplyOrderWorkflowRules.CanEditContent(e.Status))
        {
            TempData["Info"] = "لا يمكن تعديل طلب مُنفَّذ أو مغلق أو ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(new SupplyOrderFormViewModel
        {
            Id = e.Id,
            OrgUnitId = e.OrgUnitId,
            Items = e.Items,
            OrderedAt = DateTimeLocalInput.TruncateToMinute(e.OrderedAt),
            Status = e.Status,
            RequestedBy = e.RequestedBy
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, SupplyOrderFormViewModel model)
    {
        if (id != model.Id) return NotFound();
        var e = await db.SupplyOrders.FindAsync(id);
        if (e is null) return NotFound();
        if (!SupplyOrderWorkflowRules.CanEditContent(e.Status))
        {
            TempData["Info"] = "لا يمكن تعديل طلب مُنفَّذ أو مغلق أو ملغى";
            return RedirectToAction(nameof(Details), new { id });
        }
        model.OrderedAt = DateTimeLocalInput.TruncateToMinute(model.OrderedAt);
        if (!ModelState.IsValid)
        {
            model.Status = e.Status;
            model.RequestedBy = e.RequestedBy;
            return View(model);
        }
        e.OrgUnitId = model.OrgUnitId;
        e.Items = model.Items;
        e.OrderedAt = model.OrderedAt;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLine(Guid id, string? sku, int qtyRequested, decimal? unitCost, bool isCapitalizable, Guid? inventoryItemId)
    {
        var order = await db.SupplyOrders.FindAsync(id);
        if (order is null) return NotFound();
        if (!SupplyOrderWorkflowRules.CanEditContent(order.Status))
        {
            AdminLogisticsFlash.Error(TempData, "لا يمكن إضافة بنود لطلب في حالته الحالية");
            return RedirectToAction(nameof(Details), new { id });
        }
        if (qtyRequested <= 0)
        {
            AdminLogisticsFlash.Error(TempData, "الكمية يجب أن تكون أكبر من صفر");
            return RedirectToAction(nameof(Details), new { id });
        }

        db.SupplyOrderLines.Add(new SupplyOrderLine
        {
            SupplyOrderId = id,
            Sku = sku?.Trim(),
            QtyRequested = qtyRequested,
            UnitCost = unitCost,
            IsCapitalizable = isCapitalizable,
            InventoryItemId = inventoryItemId
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تمت إضافة البند";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        var e = await db.SupplyOrders.FindAsync(id);
        if (e is null) return NotFound();
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid? approverId = Guid.TryParse(rawUserId, out var uid) ? uid : null;
        if (!SupplyOrderWorkflowRules.CanApprove(e, approverId))
        {
            var message = !SupplyOrderWorkflowRules.CanApprove(e.Status)
                ? "لا يمكن الموافقة على الطلب في حالته الحالية"
                : "لا يمكن للطالب الموافقة على طلبه (فصل الم duties)";
            AdminLogisticsFlash.Error(TempData, message);
            return RedirectToAction(nameof(Details), new { id });
        }

        e.Status = SupplyOrderStatus.Approved;
        await db.SaveChangesAsync();
        TempData["Success"] = "تمت الموافقة على الطلب";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Fulfill(Guid id)
    {
        var e = await db.SupplyOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id);
        if (e is null) return NotFound();
        if (!SupplyOrderWorkflowRules.CanFulfill(e))
        {
            AdminLogisticsFlash.Error(TempData, e.Status == SupplyOrderStatus.Approved
                ? "أضف بنداً واحداً على الأقل أو تفاصيل الطلب قبل التنفيذ"
                : "لا يمكن تنفيذ الطلب في حالته الحالية");
            return RedirectToAction(nameof(Details), new { id });
        }
        e.Status = SupplyOrderStatus.Fulfilled;
        e.FulfilledAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم تنفيذ الطلب — أكّد كميات الاستلام";
        return RedirectToAction("CreateFromOrder", "ReceiptRecords", new { supplyOrderId = id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(Guid id)
    {
        var e = await db.SupplyOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id);
        if (e is null) return NotFound();

        var receiptCount = await db.ReceiptRecords.AsNoTracking().CountAsync(r => r.SupplyOrderId == id);
        if (!SupplyOrderWorkflowRules.CanClose(e, receiptCount))
        {
            AdminLogisticsFlash.Error(TempData, "لا يمكن إغلاق الطلب في حالته الحالية");
            return RedirectToAction(nameof(Details), new { id });
        }

        e.Status = SupplyOrderStatus.Closed;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إغلاق الطلب";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var e = await db.SupplyOrders.FindAsync(id);
        if (e is null) return NotFound();
        if (!SupplyOrderWorkflowRules.CanCancel(e.Status))
        {
            AdminLogisticsFlash.Error(TempData, "لا يمكن إلغاء الطلب في حالته الحالية");
            return RedirectToAction(nameof(Details), new { id });
        }
        e.Status = SupplyOrderStatus.Cancelled;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إلغاء الطلب";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class VendorsController(AdminLogisticsDbContext db) : Controller
{
    public async Task<IActionResult> Index(
        string? q,
        string? country,
        bool hasContactOnly = false,
        bool missingContactOnly = false,
        int page = 1,
        int pageSize = SearchFilterBase.DefaultPageSize)
    {
        var filter = new VendorIndexFilter
        {
            Q = q,
            Country = country,
            HasContactOnly = hasContactOnly,
            MissingContactOnly = missingContactOnly,
            Page = page,
            PageSize = pageSize
        };
        var (normalizedPage, normalizedPageSize) = filter.NormalizePaging();

        var countryOptions = await db.Vendors.AsNoTracking()
            .Where(v => v.Country != null && v.Country != "")
            .Select(v => v.Country!)
            .Distinct()
            .OrderBy(c => c)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .ToListAsync();
        ViewBag.Countries = new SelectList(countryOptions, country);

        var paged = await filter.Apply(db.Vendors.AsNoTracking())
            .OrderBy(x => x.Name)
            .ToPaginatedListAsync(normalizedPage, normalizedPageSize, HttpContext.RequestAborted);

        return View(new VendorIndexViewModel
        {
            Items = paged,
            Q = q,
            Country = country,
            HasContactOnly = hasContactOnly,
            MissingContactOnly = missingContactOnly
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.Vendors.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public IActionResult Create() => View(new VendorFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VendorFormViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        db.Vendors.Add(new Vendor
        {
            Name = model.Name,
            ContactInfo = model.ContactInfo,
            Country = model.Country
        });
        await db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة المورد";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.Vendors.FindAsync(id);
        if (e is null) return NotFound();
        return View(new VendorFormViewModel
        {
            Id = e.Id,
            Name = e.Name,
            ContactInfo = e.ContactInfo,
            Country = e.Country
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, VendorFormViewModel model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);
        var e = await db.Vendors.FindAsync(id);
        if (e is null) return NotFound();
        e.Name = model.Name;
        e.ContactInfo = model.ContactInfo;
        e.Country = model.Country;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var e = await db.Vendors.FindAsync(id);
        if (e is null) return NotFound();
        db.Vendors.Remove(e);
        await db.SaveChangesAsync();
        TempData["Success"] = "تم الحذف";
        return RedirectToAction(nameof(Index));
    }
}

public class AssetAssignmentsController(AdminLogisticsDbContext db, IEmployeeLookup employeeLookup) : Controller
{
    public async Task<IActionResult> Index(
        string? assetQ,
        Guid? employeeId,
        AssetAssignmentStatus? status,
        bool openOnly = false,
        bool overdueOnly = false,
        DateTime? assignedFrom = null,
        DateTime? assignedTo = null,
        int page = 1,
        int pageSize = SearchFilterBase.DefaultPageSize)
    {
        var scope = await OrgScopeResolver.GetAllowedOrgUnitIdsAsync(User, db.OrgUnitSnapshots, HttpContext.RequestAborted);
        if (this.ForbidIfOrgScopeDenied(scope) is { } scopeDenied)
            return scopeDenied;

        var filter = new AssetAssignmentIndexFilter
        {
            AssetQ = assetQ,
            EmployeeId = employeeId,
            Status = status,
            OpenOnly = openOnly,
            OverdueOnly = overdueOnly,
            AssignedFrom = assignedFrom,
            AssignedTo = assignedTo,
            Page = page,
            PageSize = pageSize
        };
        var (normalizedPage, normalizedPageSize) = filter.NormalizePaging();

        var assignmentQuery = filter.Apply(
            db.AssetAssignments.AsNoTracking().Include(a => a.Asset));
        if (scope.MustFilter)
        {
            var allowedEmployees = db.EmployeeOrgUnitSnapshots.AsNoTracking()
                .Where(e => e.PrimaryOrgUnitId.HasValue && scope.AllowedOrgUnitIds.Contains(e.PrimaryOrgUnitId.Value))
                .Select(e => e.EmployeeId);
            assignmentQuery = assignmentQuery.Where(a => allowedEmployees.Contains(a.EmployeeId));
        }

        var paged = await assignmentQuery
            .OrderByDescending(x => x.AssignedAt)
            .ToPaginatedListAsync(normalizedPage, normalizedPageSize, HttpContext.RequestAborted);

        ViewBag.EmployeeNames = await employeeLookup.GetEmployeeDisplayNamesAsync(
            paged.Items.Select(i => i.EmployeeId), HttpContext.RequestAborted);

        string? filterEmployeeName = null;
        if (employeeId is Guid emp && emp != Guid.Empty)
            filterEmployeeName = await employeeLookup.GetEmployeeDisplayNameAsync(emp, HttpContext.RequestAborted);

        return View(new AssetAssignmentIndexViewModel
        {
            Items = paged,
            AssetQ = assetQ,
            EmployeeId = employeeId is Guid id && id != Guid.Empty ? id : null,
            FilterEmployeeName = filterEmployeeName,
            Status = status,
            OpenOnly = openOnly,
            OverdueOnly = overdueOnly,
            AssignedFrom = assignedFrom,
            AssignedTo = assignedTo
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.AssetAssignments.Include(a => a.Asset).FirstOrDefaultAsync(a => a.Id == id);
        if (e is null) return NotFound();
        if (await this.ForbidIfEmployeeOutOfScopeForCustodyAsync(db, e.EmployeeId, HttpContext.RequestAborted) is { } denied)
            return denied;

        ViewBag.EmployeeName = await employeeLookup.GetEmployeeDisplayNameAsync(e.EmployeeId, HttpContext.RequestAborted);
        return View(e);
    }

    public IActionResult Create()
    {
        TempData["Info"] = "لتسليم عهدة جديدة، افتح الأصل من صفحة الأصول ثم استخدم «تسليم عهدة لموظف».";
        return RedirectToAction("Index", "Assets");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName(nameof(Create))]
    public IActionResult CreatePost()
    {
        TempData["Info"] = "لتسليم عهدة جديدة، افتح الأصل من صفحة الأصول ثم استخدم «تسليم عهدة لموظف».";
        return RedirectToAction("Index", "Assets");
    }

    public IActionResult Edit(Guid id)
    {
        TempData["Info"] = "التعديل والإجراءات التشغيلية (تسليم، استرداد) تتم من صفحة الأصل.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName(nameof(Edit))]
    public IActionResult EditPost(Guid id)
    {
        TempData["Info"] = "التعديل والإجراءات التشغيلية (تسليم، استرداد) تتم من صفحة الأصل.";
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class MaintenanceRecordsController(AdminLogisticsDbContext db) : Controller
{
    public async Task<IActionResult> Index(
        string? q,
        Guid? assetId,
        MaintenanceType? maintenanceType,
        DateTime? performedFrom,
        DateTime? performedTo,
        int page = 1,
        int pageSize = SearchFilterBase.DefaultPageSize)
    {
        var filter = new MaintenanceRecordIndexFilter
        {
            Q = q,
            AssetId = assetId is Guid id && id != Guid.Empty ? id : null,
            MaintenanceType = maintenanceType,
            PerformedFrom = performedFrom,
            PerformedTo = performedTo,
            Page = page,
            PageSize = pageSize
        };
        var (normalizedPage, normalizedPageSize) = filter.NormalizePaging();

        string? filterAssetLabel = null;
        if (filter.AssetId is Guid filterAssetId)
        {
            var asset = await db.Assets.AsNoTracking()
                .Where(a => a.Id == filterAssetId)
                .Select(a => new { a.TagNumber, a.Name })
                .FirstOrDefaultAsync();
            filterAssetLabel = asset is null
                ? filterAssetId.ToString()
                : $"{asset.TagNumber} — {asset.Name}";
        }

        var paged = await filter.Apply(
                db.MaintenanceRecords.AsNoTracking().Include(m => m.Asset))
            .OrderByDescending(x => x.PerformedAt)
            .ToPaginatedListAsync(normalizedPage, normalizedPageSize, HttpContext.RequestAborted);

        return View(new MaintenanceRecordIndexViewModel
        {
            Items = paged,
            Q = q,
            AssetId = filter.AssetId,
            FilterAssetLabel = filterAssetLabel,
            MaintenanceType = maintenanceType,
            PerformedFrom = performedFrom,
            PerformedTo = performedTo
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var e = await db.MaintenanceRecords.Include(m => m.Asset).FirstOrDefaultAsync(m => m.Id == id);
        if (e is null) return NotFound();
        return View(e);
    }

    public async Task<IActionResult> Create(Guid? assetId = null)
    {
        if (!IsValidAssetId(assetId))
        {
            TempData["Error"] = "يجب فتح صفحة الأصل لتسجيل صيانة جديدة.";
            return RedirectToAction(nameof(Index));
        }

        var model = new MaintenanceRecordFormViewModel
        {
            AssetId = assetId!.Value,
            PerformedAt = DateTime.Now
        };
        await PrepareMaintenanceCreateViewAsync(model, assetId.Value);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MaintenanceRecordFormViewModel model, Guid? assetId = null)
    {
        if (!IsValidAssetId(assetId))
        {
            TempData["Error"] = "يجب فتح صفحة الأصل لتسجيل صيانة جديدة.";
            return RedirectToAction(nameof(Index));
        }

        ApplyLockedMaintenanceAsset(model, assetId);
        ClearAssetIdModelState(ModelState);

        if (!ModelState.IsValid)
        {
            ViewData["ShowValidationDialog"] = true;
            await PrepareMaintenanceCreateViewAsync(model, assetId!.Value);
            return View(model);
        }

        var entity = new MaintenanceRecord
        {
            AssetId = model.AssetId,
            MaintenanceType = model.MaintenanceType,
            PerformedAt = model.PerformedAt,
            PerformedBy = model.PerformedBy,
            Cost = model.Cost,
            Notes = model.Notes
        };
        db.MaintenanceRecords.Add(entity);

        var asset = await db.Assets.FindAsync(entity.AssetId);
        if (asset is not null && asset.Status != AssetStatus.InUse)
            asset.Status = AssetStatus.Maintenance;

        await db.SaveChangesAsync();
        TempData["Success"] = "تم تسجيل الصيانة";
        return RedirectToAction("Details", "Assets", new { id = entity.AssetId });
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.MaintenanceRecords.FindAsync(id);
        if (e is null) return NotFound();
        var model = new MaintenanceRecordFormViewModel
        {
            Id = e.Id,
            AssetId = e.AssetId,
            MaintenanceType = e.MaintenanceType,
            PerformedAt = e.PerformedAt,
            PerformedBy = e.PerformedBy,
            Cost = e.Cost,
            Notes = e.Notes
        };
        await PrepareMaintenanceEditViewAsync(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, MaintenanceRecordFormViewModel model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewData["ShowValidationDialog"] = true;
            await PrepareMaintenanceEditViewAsync(model);
            return View(model);
        }
        var e = await db.MaintenanceRecords.FindAsync(id);
        if (e is null) return NotFound();
        e.AssetId = model.AssetId;
        e.MaintenanceType = model.MaintenanceType;
        e.PerformedAt = model.PerformedAt;
        e.PerformedBy = model.PerformedBy;
        e.Cost = model.Cost;
        e.Notes = model.Notes;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }

    private static bool IsValidAssetId(Guid? assetId) =>
        assetId is Guid id && id != Guid.Empty;

    private async Task PrepareMaintenanceCreateViewAsync(MaintenanceRecordFormViewModel model, Guid assetId)
    {
        model.AssetId = assetId;
        var asset = await db.Assets.AsNoTracking()
            .Where(a => a.Id == assetId)
            .Select(a => new { a.TagNumber, a.Name })
            .FirstOrDefaultAsync();
        ViewBag.LockedAssetId = assetId;
        ViewBag.LockedAssetLabel = asset is null ? assetId.ToString() : $"{asset.TagNumber} — {asset.Name}";
    }

    private async Task PrepareMaintenanceEditViewAsync(MaintenanceRecordFormViewModel model)
    {
        var selectedId = model.AssetId != Guid.Empty ? model.AssetId : (Guid?)null;
        await LoadMaintenanceAssetSelectListAsync(selectedId);
    }

    private static void ApplyLockedMaintenanceAsset(MaintenanceRecordFormViewModel model, Guid? lockedAssetId)
    {
        if (lockedAssetId is Guid assetId && assetId != Guid.Empty)
            model.AssetId = assetId;
    }

    private static void ClearAssetIdModelState(ModelStateDictionary modelState)
    {
        modelState.Remove(nameof(MaintenanceRecord.AssetId));
        modelState.Remove("AssetId");
    }

    private async Task LoadMaintenanceAssetSelectListAsync(Guid? selectedId = null)
    {
        var assets = await db.Assets.AsNoTracking()
            .OrderBy(a => a.TagNumber)
            .Take(SearchFilterBase.LookupMaxPageSize)
            .Select(a => new { a.Id, a.TagNumber })
            .ToListAsync();

        if (selectedId is Guid id && assets.All(a => a.Id != id))
        {
            var selected = await db.Assets.AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => new { a.Id, a.TagNumber })
                .FirstOrDefaultAsync();
            if (selected is not null)
                assets.Insert(0, selected);
        }

        ViewBag.Assets = new SelectList(assets, "Id", "TagNumber", selectedId);
    }
}

public class ReceiptRecordsController(AdminLogisticsDbContext db, IEmployeeLookup employeeLookup) : Controller
{
    public async Task<IActionResult> Index(
        string? q,
        Guid? supplyOrderId,
        Guid? receivedBy,
        DateTime? receivedFrom,
        DateTime? receivedTo,
        int page = 1,
        int pageSize = SearchFilterBase.DefaultPageSize)
    {
        var filter = new ReceiptRecordIndexFilter
        {
            Q = q,
            SupplyOrderId = supplyOrderId is Guid soId && soId != Guid.Empty ? soId : null,
            ReceivedBy = receivedBy is Guid rb && rb != Guid.Empty ? rb : null,
            ReceivedFrom = receivedFrom,
            ReceivedTo = receivedTo,
            Page = page,
            PageSize = pageSize
        };
        var (normalizedPage, normalizedPageSize) = filter.NormalizePaging();

        string? filterSupplyOrderSummary = null;
        if (filter.SupplyOrderId is Guid orderId)
        {
            filterSupplyOrderSummary = await db.SupplyOrders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => o.Items)
                .FirstOrDefaultAsync();
        }

        string? filterReceivedByName = null;
        if (filter.ReceivedBy is Guid empId)
            filterReceivedByName = await employeeLookup.GetEmployeeDisplayNameAsync(empId, HttpContext.RequestAborted);

        var paged = await filter.Apply(
                db.ReceiptRecords.AsNoTracking()
                    .Include(r => r.SupplyOrder)
                    .Include(r => r.InventoryItem))
            .OrderByDescending(x => x.ReceivedAt)
            .ToPaginatedListAsync(normalizedPage, normalizedPageSize, HttpContext.RequestAborted);

        var receiptIds = paged.Items.Select(r => r.Id).ToList();
        var assetCounts = receiptIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.Assets.AsNoTracking()
                .Where(a => a.OriginReceiptRecordId != null && receiptIds.Contains(a.OriginReceiptRecordId.Value))
                .GroupBy(a => a.OriginReceiptRecordId!.Value)
                .Select(g => new { ReceiptId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ReceiptId, x => x.Count);

        return View(new ReceiptRecordIndexViewModel
        {
            Items = paged,
            AssetCounts = assetCounts,
            Q = q,
            SupplyOrderId = filter.SupplyOrderId,
            FilterSupplyOrderSummary = filterSupplyOrderSummary,
            ReceivedBy = filter.ReceivedBy,
            FilterReceivedByName = filterReceivedByName,
            ReceivedFrom = receivedFrom,
            ReceivedTo = receivedTo
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var receipt = await db.ReceiptRecords
            .Include(r => r.SupplyOrder)
                .ThenInclude(o => o!.Lines)
                    .ThenInclude(l => l.InventoryItem)
            .Include(r => r.InventoryItem)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (receipt is null) return NotFound();

        var linkedAssets = await db.Assets.AsNoTracking()
            .Where(a => a.OriginReceiptRecordId == id)
            .OrderBy(a => a.TagNumber)
            .ToListAsync();

        var lineIds = receipt.SupplyOrder?.Lines
            .Where(l => l.IsCapitalizable && l.QtyReceived > 0)
            .Select(l => l.Id)
            .ToList() ?? [];

        var assetsByLine = lineIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.Assets.AsNoTracking()
                .Where(a => a.OriginSupplyOrderLineId != null && lineIds.Contains(a.OriginSupplyOrderLineId.Value))
                .GroupBy(a => a.OriginSupplyOrderLineId!.Value)
                .Select(g => new { LineId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.LineId, x => x.Count);

        var capitalizableLines = receipt.SupplyOrder?.Lines
            .Where(l => l.IsCapitalizable && l.QtyReceived > 0)
            .Select(l => new ReceiptCapitalizableLineViewModel
            {
                LineId = l.Id,
                Label = l.Sku ?? l.InventoryItem?.Name ?? "—",
                QtyReceived = l.QtyReceived,
                AssetsCreated = assetsByLine.GetValueOrDefault(l.Id),
                UnitCost = l.UnitCost
            })
            .ToList() ?? [];

        var expectedCapitalizable = capitalizableLines.Sum(l => l.QtyReceived);

        string? receivedByName = null;
        if (receipt.ReceivedBy is Guid rb)
            receivedByName = await employeeLookup.GetEmployeeDisplayNameAsync(rb, HttpContext.RequestAborted);

        return View(new ReceiptDetailsViewModel
        {
            Receipt = receipt,
            LinkedAssetCount = linkedAssets.Count,
            ExpectedCapitalizableCount = expectedCapitalizable,
            CapitalizableLines = capitalizableLines,
            LinkedAssets = linkedAssets,
            ReceivedByName = receivedByName
        });
    }

    public async Task<IActionResult> CreateFromOrder(Guid supplyOrderId)
    {
        var order = await db.SupplyOrders
            .Include(o => o.Lines)
            .ThenInclude(l => l.InventoryItem)
            .FirstOrDefaultAsync(o => o.Id == supplyOrderId);
        if (order is null) return NotFound();
        if (!SupplyReceiptRules.CanReceiveFromOrder(order))
        {
            AdminLogisticsFlash.Error(TempData, "لا يمكن تسجيل الاستلام إلا لطلب منفّذ");
            return RedirectToAction("Details", "SupplyOrders", new { id = supplyOrderId });
        }

        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        Guid? receivedBy = Guid.TryParse(rawUserId, out var uid) ? uid : null;

        var model = new CreateReceiptFromOrderViewModel
        {
            SupplyOrderId = order.Id,
            OrderSummary = order.Items,
            ReceivedAt = DateTimeLocalInput.TruncateToMinute(DateTime.UtcNow),
            ReceivedBy = receivedBy,
            Lines = order.Lines.Count > 0
                ? order.Lines.Select(l => new ReceiptLineInputViewModel
                {
                    LineId = l.Id,
                    Sku = l.Sku ?? l.InventoryItem?.Sku,
                    QtyRequested = l.QtyRequested,
                    QtyReceived = l.QtyRequested,
                    IsCapitalizable = l.IsCapitalizable,
                    InventoryItemId = l.InventoryItemId
                }).ToList()
                : []
        };

        if (model.Lines.Count == 0)
            ViewBag.NoLinesFallback = true;

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFromOrder(CreateReceiptFromOrderViewModel model)
    {
        var order = await db.SupplyOrders
            .Include(o => o.Lines)
            .ThenInclude(l => l.InventoryItem)
            .FirstOrDefaultAsync(o => o.Id == model.SupplyOrderId);
        if (order is null) return NotFound();
        if (!SupplyReceiptRules.CanReceiveFromOrder(order))
        {
            AdminLogisticsFlash.Error(TempData, "لا يمكن تسجيل الاستلام إلا لطلب منفّذ");
            return RedirectToAction("Details", "SupplyOrders", new { id = model.SupplyOrderId });
        }

        var qtyByLine = model.Lines
            .Where(l => l.LineId != Guid.Empty && l.QtyReceived > 0)
            .ToDictionary(l => l.LineId, l => l.QtyReceived);

        if (qtyByLine.Count == 0 && order.Lines.Count > 0)
        {
            ModelState.AddModelError(string.Empty, "أدخل كمية مستلمة لبند واحد على الأقل");
            model.OrderSummary = order.Items;
            return View(model);
        }

        var draft = SupplyReceiptRules.BuildReceiptDraft(order, model.ReceivedBy, model.ReceivedAt);
        db.ReceiptRecords.Add(draft);

        if (order.Lines.Count > 0)
        {
            SupplyReceiptRules.ApplyReceivedQuantitiesToLines(order, qtyByLine);
            await ReceiptInventoryService.ApplyReceiptLinesAsync(db, order, qtyByLine, HttpContext.RequestAborted);

            var firstLine = order.Lines.FirstOrDefault(l => qtyByLine.ContainsKey(l.Id));
            if (firstLine?.InventoryItemId is Guid invId)
            {
                draft.InventoryItemId = invId;
                draft.QuantityReceived = qtyByLine.GetValueOrDefault(firstLine.Id);
            }
        }

        if (SupplyReceiptRules.IsFullyReceived(order))
        {
            order.Status = SupplyOrderStatus.Closed;
            TempData["Success"] = "تم تسجيل الاستلام وإغلاق الطلب بعد اكتمال الاستلام";
        }
        else
        {
            TempData["Success"] = "تم تسجيل الاستلام";
        }

        await db.SaveChangesAsync();
        TempData["NextStep"] = "الخطوة التالية: إنشاء أصول من البنود القابلة للرسملة";
        return RedirectToAction(nameof(Details), new { id = draft.Id });
    }

    public IActionResult Create()
    {
        TempData["Error"] = "يجب فتح طلب التجهيز المنفّذ لتسجيل استلام جديد.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName(nameof(Create))]
    public IActionResult CreatePost()
    {
        TempData["Error"] = "يجب فتح طلب التجهيز المنفّذ لتسجيل استلام جديد.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var e = await db.ReceiptRecords.FindAsync(id);
        if (e is null) return NotFound();
        ViewBag.Orders = new SelectList(await db.SupplyOrders.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Items");
        ViewBag.InventoryItems = new SelectList(await db.InventoryItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Name", e.InventoryItemId);
        return View(new ReceiptRecordFormViewModel
        {
            Id = e.Id,
            SupplyOrderId = e.SupplyOrderId,
            ReceivedBy = e.ReceivedBy,
            ReceivedAt = e.ReceivedAt,
            ItemsReceived = e.ItemsReceived,
            InventoryItemId = e.InventoryItemId,
            QuantityReceived = e.QuantityReceived
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, ReceiptRecordFormViewModel model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewBag.Orders = new SelectList(await db.SupplyOrders.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Items");
            ViewBag.InventoryItems = new SelectList(await db.InventoryItems.AsNoTracking().Take(SearchFilterBase.LookupMaxPageSize).ToListAsync(), "Id", "Name");
            return View(model);
        }
        var e = await db.ReceiptRecords.FindAsync(id);
        if (e is null) return NotFound();
        e.SupplyOrderId = model.SupplyOrderId;
        e.ReceivedBy = model.ReceivedBy;
        e.ReceivedAt = model.ReceivedAt;
        e.ItemsReceived = model.ItemsReceived;
        e.InventoryItemId = model.InventoryItemId;
        e.QuantityReceived = model.QuantityReceived;
        await db.SaveChangesAsync();
        TempData["Success"] = "تم التحديث";
        return RedirectToAction(nameof(Details), new { id });
    }
}
