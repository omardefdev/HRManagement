using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using Admin.Logistics.Web.Services;
using Admin.Logistics.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;

namespace HRManagement.UnitTests;

public class AssetHandoverRulesTests
{
    [Fact]
    public void HasProof_accepts_file_only()
    {
        Assert.True(AssetHandoverRules.HasProof(null, Guid.NewGuid()));
    }

    [Fact]
    public void HasProof_accepts_text_only()
    {
        Assert.True(AssetHandoverRules.HasProof("ab", null));
        Assert.True(AssetHandoverRules.HasProof("EVD-2026-001", null));
    }

    [Fact]
    public void HasProof_rejects_missing_proof()
    {
        Assert.False(AssetHandoverRules.HasProof(null, null));
        Assert.False(AssetHandoverRules.HasProof("", null));
        Assert.False(AssetHandoverRules.HasProof(" ", null));
        Assert.False(AssetHandoverRules.HasProof("a", null));
        Assert.False(AssetHandoverRules.HasProof("موقّع", null));
    }
}

public class AssetDetailsFormDraftStoreTests
{
    private static TempDataDictionary CreateTempData()
    {
        var httpContext = new DefaultHttpContext();
        return new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
    }

    [Fact]
    public void SaveAssignDraft_and_Load_roundtrip()
    {
        var tempData = CreateTempData();
        var assetId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var draft = new AssignToEmployeeDraft
        {
            EmployeeId = employeeId,
            DueBackDate = new DateTime(2026, 8, 10),
            HandoverCondition = AssetHandoverCondition.Good,
            HandoverConditionNotes = "ملاحظة",
            EvidenceRef = "EVD-1",
            HandoverFileId = fileId
        };

        AssetDetailsFormDraftStore.SaveAssignDraft(tempData, assetId, draft);
        var state = AssetDetailsFormDraftStore.Load(tempData, assetId);

        Assert.NotNull(state.Assign);
        Assert.Equal(employeeId, state.Assign!.EmployeeId);
        Assert.Equal("EVD-1", state.Assign.EvidenceRef);
        Assert.Equal(fileId, state.Assign.HandoverFileId);
    }

    [Fact]
    public void SetFormError_persists_message_and_scroll_target()
    {
        var tempData = CreateTempData();
        var assetId = Guid.NewGuid();
        const string message = "اختر موظفاً قبل التسليم";

        AssetDetailsFormDraftStore.SetFormError(tempData, assetId, AssetDetailsFormKeys.Assign, message);
        var state = AssetDetailsFormDraftStore.Load(tempData, assetId);

        Assert.Equal(message, state.FormErrorAssign);
        Assert.Equal(AssetDetailsFormKeys.Assign, state.ScrollToForm);
        Assert.Equal(message, tempData["Error"]);
        Assert.Equal("true", tempData[AssetDetailsFormDraftStore.ShowDialogKey]);
    }

    [Fact]
    public void SetDialogError_persists_error_and_show_dialog_flag()
    {
        var tempData = CreateTempData();
        const string message = "لا يمكن استرداد هذا الأصل";

        AssetDetailsFormDraftStore.SetDialogError(tempData, message);

        Assert.Equal(message, tempData["Error"]);
        Assert.Equal("true", tempData[AssetDetailsFormDraftStore.ShowDialogKey]);
    }

    [Fact]
    public void ApplyToViewBag_exposes_draft_and_errors()
    {
        var tempData = CreateTempData();
        var viewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(
            new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
            new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
        {
            Model = new object()
        };
        var assetId = Guid.NewGuid();
        AssetDetailsFormDraftStore.SaveAssignDraft(tempData, assetId, new AssignToEmployeeDraft { EvidenceRef = "X" });
        AssetDetailsFormDraftStore.SetFormError(tempData, assetId, AssetDetailsFormKeys.Assign, "خطأ");

        AssetDetailsFormDraftStore.ApplyToViewBag(viewData, tempData, assetId);

        Assert.Equal("X", (viewData["AssignDraft"] as AssignToEmployeeDraft)?.EvidenceRef);
        Assert.Equal("خطأ", viewData["FormErrorAssign"] as string);
        Assert.Equal(AssetDetailsFormKeys.Assign, viewData["FormScrollTo"] as string);
    }
}

public class AssetDetailsFormValidationTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void IndicatesUploadInProgress(string? flag, bool expected)
    {
        Assert.Equal(expected, AssetDetailsFormValidation.IndicatesUploadInProgress(flag));
    }
}
