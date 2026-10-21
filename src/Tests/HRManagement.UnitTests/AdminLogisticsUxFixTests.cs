using System.Reflection;
using System.Text.RegularExpressions;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

/// <summary>Admin Logistics UX fixes: receipt display, maintenance create, COD-001 tag immutability.</summary>
public class AdminLogisticsUxFixTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "src", "Gateway", "HRManagement.Gateway", "Program.cs")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void Assets_Edit_view_shows_receipt_label_not_raw_guid()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "Assets", "Edit.cshtml");
        var source = System.IO.File.ReadAllText(path);

        Assert.Contains("OriginReceiptReceivedAt", source, StringComparison.Ordinal);
        Assert.Contains("OriginReceiptItemsReceived", source, StringComparison.Ordinal);
        Assert.False(
            Regex.IsMatch(source, @">\s*@receiptId\s*<", RegexOptions.Multiline),
            "Receipt link text must not be raw GUID");
    }

    [Fact]
    public void Assets_Edit_view_locks_TagNumber_readonly()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "Assets", "Edit.cshtml");
        var source = System.IO.File.ReadAllText(path);

        Assert.Contains("asp-for=\"TagNumber\"", source, StringComparison.Ordinal);
        Assert.Contains("readonly", source, StringComparison.Ordinal);
        Assert.Contains("COD-001", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetsController_Edit_POST_does_not_validate_or_persist_TagNumber()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AssetsController.cs");
        var source = System.IO.File.ReadAllText(path);

        var editPost = ExtractMethod(source, "public async Task<IActionResult> Edit(Guid id, AssetEditFormViewModel model)");
        Assert.DoesNotContain("ValidateUniqueTagNumberAsync", editPost, StringComparison.Ordinal);
        Assert.DoesNotContain("e.TagNumber = entity.TagNumber", editPost, StringComparison.Ordinal);
        Assert.Contains("TagNumber immutable", editPost, StringComparison.Ordinal);
    }

    [Fact]
    public void MaintenanceRecords_Create_view_exposes_field_validation()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "MaintenanceRecords", "Create.cshtml");
        var source = System.IO.File.ReadAllText(path);

        Assert.Contains("type=\"hidden\" asp-for=\"AssetId\"", source, StringComparison.Ordinal);
        Assert.Contains("_ValidationDialogTrigger", source, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-items=\"assets\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("— اختر أصل —", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MaintenanceRecords_Index_has_no_create_button()
    {
        var indexPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "MaintenanceRecords", "Index.cshtml");
        var source = System.IO.File.ReadAllText(indexPath);

        Assert.DoesNotContain("asp-action=\"Create\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain(">إضافة<", source, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"Assets\" asp-action=\"Index\"", source, StringComparison.Ordinal);
        Assert.Contains("إضافة صيانة", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MaintenanceRecords_Index_has_filter_form_and_fields()
    {
        var indexPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "MaintenanceRecords", "Index.cshtml");
        var indexSource = System.IO.File.ReadAllText(indexPath);

        Assert.Contains("method=\"get\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"q\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"maintenanceType\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"performedFrom\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"performedTo\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("MaintenanceRecordIndexViewModel", indexSource, StringComparison.Ordinal);

        var controllerPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AdminExtendedControllers.cs");
        var controllerSource = System.IO.File.ReadAllText(controllerPath);
        var controller = ExtractClass(controllerSource, "public class MaintenanceRecordsController");
        var indexAction = ExtractMethod(controller, "public async Task<IActionResult> Index(");

        Assert.Contains("MaintenanceRecordIndexFilter", indexAction, StringComparison.Ordinal);
        Assert.Contains("MaintenanceRecordIndexViewModel", indexAction, StringComparison.Ordinal);
    }

    [Fact]
    public void MaintenanceRecordsController_Create_requires_assetId()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AdminExtendedControllers.cs");
        var source = System.IO.File.ReadAllText(path);
        var controller = ExtractClass(source, "public class MaintenanceRecordsController");

        var createGet = ExtractMethod(controller, "public async Task<IActionResult> Create(Guid? assetId = null)");
        var createPost = ExtractMethod(controller, "public async Task<IActionResult> Create(MaintenanceRecordFormViewModel model, Guid? assetId = null)");

        Assert.Contains("IsValidAssetId", createGet, StringComparison.Ordinal);
        Assert.Contains("RedirectToAction(nameof(Index))", createGet, StringComparison.Ordinal);

        Assert.Contains("IsValidAssetId", createPost, StringComparison.Ordinal);
        Assert.Contains("RedirectToAction(nameof(Index))", createPost, StringComparison.Ordinal);
    }

    [Fact]
    public void MaintenanceRecord_Asset_navigation_is_nullable_for_model_validation()
    {
        var property = typeof(MaintenanceRecord).GetProperty(nameof(MaintenanceRecord.Asset));
        Assert.NotNull(property);
        Assert.True(property!.PropertyType.IsClass);
        Assert.True(new NullabilityInfoContext().Create(property).WriteState is NullabilityState.Nullable);
    }

    [Fact]
    public async Task MaintenanceRecord_can_be_persisted_for_asset()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AdminLogisticsDbContext(options);

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            TagNumber = "AH-2026-000001",
            Name = "حاسوب",
            Status = AssetStatus.InStorage
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();

        db.MaintenanceRecords.Add(new MaintenanceRecord
        {
            AssetId = asset.Id,
            MaintenanceType = MaintenanceType.Preventive,
            PerformedAt = DateTime.Now,
            PerformedBy = "فني"
        });
        await db.SaveChangesAsync();

        var saved = await db.MaintenanceRecords.SingleAsync();
        Assert.Equal(asset.Id, saved.AssetId);
        Assert.Equal(MaintenanceType.Preventive, saved.MaintenanceType);
    }

    [Fact]
    public void AssetAssignments_Index_is_read_only_with_assets_guidance()
    {
        var root = ResolveRepoRoot();
        var indexPath = Path.Combine(root, "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "AssetAssignments", "Index.cshtml");
        var source = System.IO.File.ReadAllText(indexPath);

        Assert.DoesNotContain("asp-action=\"Create\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain(">جديد<", source, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"Assets\" asp-action=\"Index\"", source, StringComparison.Ordinal);
        Assert.Contains("تسليم عهدة لموظف", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetAssignments_Index_has_comprehensive_filter_fields()
    {
        var root = ResolveRepoRoot();
        var indexPath = Path.Combine(root, "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "AssetAssignments", "Index.cshtml");
        var source = System.IO.File.ReadAllText(indexPath);

        Assert.Contains("name=\"assetQ\"", source, StringComparison.Ordinal);
        Assert.Contains("data-hr-picker=\"employee\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"overdueOnly\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder=\"GUID", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssetAssignments_Details_is_read_only_with_asset_link()
    {
        var root = ResolveRepoRoot();
        var detailsPath = Path.Combine(root, "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "AssetAssignments", "Details.cshtml");
        var source = System.IO.File.ReadAllText(detailsPath);

        Assert.DoesNotContain("asp-action=\"Edit\"", source, StringComparison.Ordinal);
        Assert.Contains("فتح صفحة الأصل", source, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"Assets\" asp-action=\"Details\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetAssignments_Create_and_Edit_views_removed()
    {
        var root = ResolveRepoRoot();
        var viewsDir = Path.Combine(root, "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "AssetAssignments");
        Assert.False(System.IO.File.Exists(Path.Combine(viewsDir, "Create.cshtml")));
        Assert.False(System.IO.File.Exists(Path.Combine(viewsDir, "Edit.cshtml")));
    }

    [Fact]
    public void AssetAssignmentsController_Create_and_Edit_redirect_instead_of_views()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AdminExtendedControllers.cs");
        var source = System.IO.File.ReadAllText(path);
        var controller = ExtractClass(source, "public class AssetAssignmentsController");

        var createGet = ExtractMethod(controller, "public IActionResult Create()");
        var createPost = ExtractMethod(controller, "public IActionResult CreatePost()");
        var editGet = ExtractMethod(controller, "public IActionResult Edit(Guid id)");
        var editPost = ExtractMethod(controller, "public IActionResult EditPost(Guid id)");

        Assert.Contains("RedirectToAction(\"Index\", \"Assets\")", createGet, StringComparison.Ordinal);
        Assert.Contains("RedirectToAction(\"Index\", \"Assets\")", createPost, StringComparison.Ordinal);
        Assert.DoesNotContain("return View", createGet, StringComparison.Ordinal);
        Assert.DoesNotContain("return View", createPost, StringComparison.Ordinal);

        Assert.Contains("RedirectToAction(nameof(Details)", editGet, StringComparison.Ordinal);
        Assert.Contains("RedirectToAction(nameof(Details)", editPost, StringComparison.Ordinal);
        Assert.DoesNotContain("return View", editGet, StringComparison.Ordinal);
        Assert.DoesNotContain("return View", editPost, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplyOrders_Create_view_has_no_status_or_fulfilled_fields()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "SupplyOrders", "Create.cshtml");
        var source = System.IO.File.ReadAllText(path);

        Assert.DoesNotContain("asp-for=\"Status\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"FulfilledAt\"", source, StringComparison.Ordinal);
        Assert.Contains("قيد الانتظار", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplyOrders_Edit_view_shows_status_readonly()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "SupplyOrders", "Edit.cshtml");
        var source = System.IO.File.ReadAllText(path);

        Assert.DoesNotContain("asp-for=\"Status\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"FulfilledAt\"", source, StringComparison.Ordinal);
        Assert.Contains("HrEnumDisplay(Model.Status)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplyOrders_Details_conditional_edit_and_close()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "SupplyOrders", "Details.cshtml");
        var source = System.IO.File.ReadAllText(path);

        Assert.Contains("canEditContent", source, StringComparison.Ordinal);
        Assert.Contains("canClose", source, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Close\"", source, StringComparison.Ordinal);
        Assert.Contains("استلام جزئي", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplyOrders_Details_has_confirm_on_approve_fulfill_and_approve_guard()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "SupplyOrders", "Details.cshtml");
        var source = System.IO.File.ReadAllText(path);

        Assert.Contains("canApprove", source, StringComparison.Ordinal);
        Assert.Contains("approveBlockedReason", source, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Approve\"", source, StringComparison.Ordinal);
        Assert.Matches("asp-action=\"Approve\"[\\s\\S]*data-hr-confirm=", source);
        Assert.Matches("asp-action=\"Fulfill\"[\\s\\S]*data-hr-confirm=", source);
    }

    [Fact]
    public void ReceiptRecords_Index_has_no_create_button()
    {
        var indexPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "ReceiptRecords", "Index.cshtml");
        var source = System.IO.File.ReadAllText(indexPath);

        Assert.DoesNotContain("asp-action=\"Create\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain(">إضافة<", source, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"SupplyOrders\" asp-action=\"Index\"", source, StringComparison.Ordinal);
        Assert.Contains("تسجيل الاستلام", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptRecords_Index_has_filter_form_and_fields()
    {
        var indexPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "ReceiptRecords", "Index.cshtml");
        var indexSource = System.IO.File.ReadAllText(indexPath);

        Assert.Contains("method=\"get\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"q\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"receivedBy\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"receivedFrom\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"receivedTo\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("ReceiptRecordIndexViewModel", indexSource, StringComparison.Ordinal);

        var controllerPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AdminExtendedControllers.cs");
        var controllerSource = System.IO.File.ReadAllText(controllerPath);
        var controller = ExtractClass(controllerSource, "public class ReceiptRecordsController");
        var indexAction = ExtractMethod(controller, "public async Task<IActionResult> Index(");

        Assert.Contains("ReceiptRecordIndexFilter", indexAction, StringComparison.Ordinal);
        Assert.Contains("ReceiptRecordIndexViewModel", indexAction, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptRecordsController_Create_redirects_to_index()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AdminExtendedControllers.cs");
        var source = System.IO.File.ReadAllText(path);
        var controller = ExtractClass(source, "public class ReceiptRecordsController");

        var createGet = ExtractMethod(controller, "public IActionResult Create()");
        var createPost = ExtractMethod(controller, "public IActionResult CreatePost()");

        Assert.Contains("RedirectToAction(nameof(Index))", createGet, StringComparison.Ordinal);
        Assert.DoesNotContain("return View", createGet, StringComparison.Ordinal);
        Assert.Contains("RedirectToAction(nameof(Index))", createPost, StringComparison.Ordinal);
        Assert.DoesNotContain("return View", createPost, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptRecords_Create_view_removed()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "ReceiptRecords", "Create.cshtml");
        Assert.False(System.IO.File.Exists(path));
    }

    [Fact]
    public void ReceiptRecords_CreateFromOrder_has_submit_confirm()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "ReceiptRecords", "CreateFromOrder.cshtml");
        var source = System.IO.File.ReadAllText(path);

        Assert.Contains("data-hr-confirm=\"تأكيد تسجيل كميات الاستلام؟\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HrAlerts_partial_has_scroll_anchor()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Shared", "HRManagement.Web.Shared", "Views", "Shared", "_HrAlerts.cshtml");
        var source = System.IO.File.ReadAllText(path);

        Assert.Contains("data-hr-alerts", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplyOrders_Index_has_filter_form_and_fields()
    {
        var indexPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "SupplyOrders", "Index.cshtml");
        var indexSource = System.IO.File.ReadAllText(indexPath);

        Assert.Contains("method=\"get\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"q\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"status\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"requestedBy\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"orgUnitId\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"activeOnly\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"awaitingReceiptOnly\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("SupplyOrderIndexViewModel", indexSource, StringComparison.Ordinal);

        var controllerPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AdminExtendedControllers.cs");
        var controllerSource = System.IO.File.ReadAllText(controllerPath);
        var controller = ExtractClass(controllerSource, "public class SupplyOrdersController");
        var indexAction = ExtractMethod(controller, "public async Task<IActionResult> Index(");

        Assert.Contains("SupplyOrderIndexFilter", indexAction, StringComparison.Ordinal);
        Assert.Contains("SupplyOrderIndexViewModel", indexAction, StringComparison.Ordinal);
    }

    [Fact]
    public void Assets_Index_has_comprehensive_filter_form_and_fields()
    {
        var indexPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "Assets", "Index.cshtml");
        var indexSource = System.IO.File.ReadAllText(indexPath);

        Assert.Contains("method=\"get\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"q\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"holderEmployeeId\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"originKind\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"unassignedOnly\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"assignedOnly\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"privilegedOnly\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"activeOnly\"", indexSource, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"noOrigin\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("AssetIndexViewModel", indexSource, StringComparison.Ordinal);

        var controllerPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AssetsController.cs");
        var controllerSource = System.IO.File.ReadAllText(controllerPath);
        var indexAction = ExtractMethod(controllerSource, "public async Task<IActionResult> Index(");

        Assert.Contains("AssetIndexFilter", indexAction, StringComparison.Ordinal);
        Assert.Contains("AssetIndexViewModel", indexAction, StringComparison.Ordinal);
    }

    [Fact]
    public void Vendors_Index_has_filter_form_and_fields()
    {
        var indexPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "Vendors", "Index.cshtml");
        var indexSource = System.IO.File.ReadAllText(indexPath);

        Assert.Contains("method=\"get\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"q\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"country\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"hasContactOnly\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("name=\"missingContactOnly\"", indexSource, StringComparison.Ordinal);
        Assert.Contains("VendorIndexViewModel", indexSource, StringComparison.Ordinal);

        var controllerPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AdminExtendedControllers.cs");
        var controllerSource = System.IO.File.ReadAllText(controllerPath);
        var controller = ExtractClass(controllerSource, "public class VendorsController");
        var indexAction = ExtractMethod(controller, "public async Task<IActionResult> Index(");

        Assert.Contains("VendorIndexFilter", indexAction, StringComparison.Ordinal);
        Assert.Contains("VendorIndexViewModel", indexAction, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplyOrdersController_enforces_workflow_on_create_edit_fulfill_close()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AdminExtendedControllers.cs");
        var source = System.IO.File.ReadAllText(path);
        var controller = ExtractClass(source, "public class SupplyOrdersController");

        var createPost = ExtractMethod(controller, "public async Task<IActionResult> Create(SupplyOrderFormViewModel model)");
        Assert.Contains("Status = SupplyOrderStatus.Pending", createPost, StringComparison.Ordinal);
        Assert.Contains("FulfilledAt = null", createPost, StringComparison.Ordinal);
        Assert.DoesNotContain("model.Status = model.Status", createPost, StringComparison.Ordinal);

        var editPost = ExtractMethod(controller, "public async Task<IActionResult> Edit(Guid id, SupplyOrderFormViewModel model)");
        Assert.Contains("CanEditContent", editPost, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Status = model.Status", editPost, StringComparison.Ordinal);
        Assert.DoesNotContain("e.FulfilledAt = model.FulfilledAt", editPost, StringComparison.Ordinal);

        var fulfill = ExtractMethod(controller, "public async Task<IActionResult> Fulfill(Guid id)");
        Assert.Contains("CanFulfill(e)", fulfill, StringComparison.Ordinal);
        Assert.Contains(".Include(o => o.Lines)", fulfill, StringComparison.Ordinal);

        var approve = ExtractMethod(controller, "public async Task<IActionResult> Approve(Guid id)");
        Assert.Contains("CanApprove(e, approverId)", approve, StringComparison.Ordinal);
        Assert.Contains("AdminLogisticsFlash.Error", approve, StringComparison.Ordinal);

        var close = ExtractMethod(controller, "public async Task<IActionResult> Close(Guid id)");
        Assert.Contains("CanClose(e, receiptCount)", close, StringComparison.Ordinal);
        Assert.Contains("SupplyOrderStatus.Closed", close, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptRecordsController_CreateFromOrder_auto_closes_when_fully_received()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AdminExtendedControllers.cs");
        var source = System.IO.File.ReadAllText(path);
        var controller = ExtractClass(source, "public class ReceiptRecordsController");
        var createPost = ExtractMethod(controller, "public async Task<IActionResult> CreateFromOrder(CreateReceiptFromOrderViewModel model)");

        Assert.Contains("IsFullyReceived(order)", createPost, StringComparison.Ordinal);
        Assert.Contains("SupplyOrderStatus.Closed", createPost, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminLogistics_all_index_views_have_pagination_partial()
    {
        var root = ResolveRepoRoot();
        var viewsDir = Path.Combine(root, "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views");
        var indexViews = new[]
        {
            Path.Combine(viewsDir, "Assets", "Index.cshtml"),
            Path.Combine(viewsDir, "InventoryItems", "Index.cshtml"),
            Path.Combine(viewsDir, "SupplyOrders", "Index.cshtml"),
            Path.Combine(viewsDir, "Vendors", "Index.cshtml"),
            Path.Combine(viewsDir, "AssetAssignments", "Index.cshtml"),
            Path.Combine(viewsDir, "MaintenanceRecords", "Index.cshtml"),
            Path.Combine(viewsDir, "ReceiptRecords", "Index.cshtml")
        };

        foreach (var path in indexViews)
        {
            var source = System.IO.File.ReadAllText(path);
            Assert.Contains("_HrPagination", source, StringComparison.Ordinal);
            Assert.Contains("name=\"page\"", source, StringComparison.Ordinal);
            Assert.Contains("Model.Items.Items", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AdminLogistics_controllers_use_ToPaginatedListAsync()
    {
        var assetsPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AssetsController.cs");
        var assetsIndex = ExtractMethod(System.IO.File.ReadAllText(assetsPath), "public async Task<IActionResult> Index(");
        Assert.Contains("ToPaginatedListAsync", assetsIndex, StringComparison.Ordinal);

        var extendedPath = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "AdminExtendedControllers.cs");
        var extendedSource = System.IO.File.ReadAllText(extendedPath);
        Assert.Contains("ToPaginatedListAsync", extendedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(SearchFilterBase.MaxPageSize)", extendedSource, StringComparison.Ordinal);
    }

    private static string ExtractClass(string source, string classStart)
    {
        var start = source.IndexOf(classStart, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Class not found: {classStart}");
        var brace = source.IndexOf('{', start);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException("Unbalanced braces");
    }

    private static string ExtractMethod(string source, string signatureStart)
    {
        var start = source.IndexOf(signatureStart, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method not found: {signatureStart}");
        var brace = source.IndexOf('{', start);
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException("Unbalanced braces");
    }
}
