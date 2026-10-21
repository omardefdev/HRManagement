using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Infrastructure.Data;
using Admin.Logistics.Web.Controllers;
using Admin.Logistics.Web.ViewModels;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

public class InventorySkuGenerationTests
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
    public void Create_view_has_no_sku_field()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "InventoryItems", "Create.cshtml");
        var source = System.IO.File.ReadAllText(path);

        Assert.DoesNotContain("asp-for=\"Sku\"", source, StringComparison.Ordinal);
        Assert.Contains("يُولَّد رمز المخزون", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_view_locks_sku_readonly()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Views", "InventoryItems", "Edit.cshtml");
        var source = System.IO.File.ReadAllText(path);

        Assert.Contains("asp-for=\"Sku\"", source, StringComparison.Ordinal);
        Assert.Contains("readonly", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryItemsController_Create_POST_generates_sku()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "InventoryItemsController.cs");
        var source = System.IO.File.ReadAllText(path);

        var createPost = ExtractMethod(source, "public async Task<IActionResult> Create(InventoryItemFormViewModel model)");
        Assert.Contains("CodeGenerationRequests.InventorySku()", createPost, StringComparison.Ordinal);
        Assert.DoesNotContain("model.Sku = sku!", createPost, StringComparison.Ordinal);
        Assert.Contains("Sku = sku!", createPost, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryItemsController_Edit_POST_does_not_persist_sku()
    {
        var path = Path.Combine(
            ResolveRepoRoot(),
            "src", "Services", "AdminLogistics", "Admin.Logistics.Web", "Controllers", "InventoryItemsController.cs");
        var source = System.IO.File.ReadAllText(path);

        var editPost = ExtractMethod(source, "public async Task<IActionResult> Edit(Guid id, InventoryItemFormViewModel model)");
        Assert.DoesNotContain("e.Sku = model.Sku", editPost, StringComparison.Ordinal);
        Assert.DoesNotContain("SetProperty(x => x.Sku", editPost, StringComparison.Ordinal);
        Assert.Contains("Sku immutable", editPost, StringComparison.Ordinal);
    }

    private static InventoryItemsController CreateController(AdminLogisticsDbContext db, ICodeGenerationClient codeClient)
    {
        var httpContext = new DefaultHttpContext();
        return new InventoryItemsController(db, codeClient)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
    }

    [Fact]
    public async Task Create_assigns_generated_sku_and_persists()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AdminLogisticsDbContext(options);

        var mock = new Mock<ICodeGenerationClient>();
        mock.Setup(c => c.GenerateAsync(
                It.Is<CodeGenerationRequest>(r => r.EntityKind == CodeEntityKind.InventorySku),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeGenerationResult { Code = "SKU-2026-000001" });

        var controller = CreateController(db, mock.Object);

        var model = new InventoryItemFormViewModel { Name = "أقلام", Quantity = 5 };
        var result = await controller.Create(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);

        var saved = await db.InventoryItems.SingleAsync();
        Assert.Equal("SKU-2026-000001", saved.Sku);
        Assert.Equal("أقلام", saved.Name);
    }

    [Fact]
    public async Task Create_fails_when_code_generation_unavailable()
    {
        var options = new DbContextOptionsBuilder<AdminLogisticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AdminLogisticsDbContext(options);

        var mock = new Mock<ICodeGenerationClient>();
        mock.Setup(c => c.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CodeGenerationResult?)null);

        var controller = CreateController(db, mock.Object);

        var model = new InventoryItemFormViewModel { Name = "أقلام", Quantity = 5 };
        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(await db.InventoryItems.AnyAsync());
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
