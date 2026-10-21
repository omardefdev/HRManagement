using System.Text.Json;
using Admin.Logistics.Web.ViewModels;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Admin.Logistics.Web.Services;

public static class AssetDetailsFormDraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public const string DraftKeyPrefix = "AssetDetailsDraft_";
    public const string ErrorKeyPrefix = "FormError_";
    public const string ScrollKey = "FormScrollTo";
    public const string ShowDialogKey = "ShowDialog";

    public static void SetFormError(ITempDataDictionary tempData, Guid assetId, string formKey, string message)
    {
        tempData[ErrorKey(assetId, formKey)] = message;
        tempData[ScrollKey] = formKey;
        SetDialogError(tempData, message);
    }

    /// <summary>TempData error shown via #hrFlashDialog (not _HrAlerts banner).</summary>
    public static void SetDialogError(ITempDataDictionary tempData, string message)
    {
        tempData["Error"] = message;
        tempData[ShowDialogKey] = "true";
    }

    public static void SaveAssignDraft(ITempDataDictionary tempData, Guid assetId, AssignToEmployeeDraft draft) =>
        tempData[DraftKey(assetId, AssetDetailsFormKeys.Assign)] = JsonSerializer.Serialize(draft, JsonOptions);

    public static void SaveReturnDraft(ITempDataDictionary tempData, Guid assetId, ReturnAssignmentDraft draft) =>
        tempData[DraftKey(assetId, AssetDetailsFormKeys.Return)] = JsonSerializer.Serialize(draft, JsonOptions);

    public static void SaveLossDraft(ITempDataDictionary tempData, Guid assetId, ReportLossDraft draft) =>
        tempData[DraftKey(assetId, AssetDetailsFormKeys.Loss)] = JsonSerializer.Serialize(draft, JsonOptions);

    public static AssetDetailsFormState Load(ITempDataDictionary tempData, Guid assetId)
    {
        return new AssetDetailsFormState
        {
            Assign = LoadDraft<AssignToEmployeeDraft>(tempData, assetId, AssetDetailsFormKeys.Assign),
            Return = LoadDraft<ReturnAssignmentDraft>(tempData, assetId, AssetDetailsFormKeys.Return),
            Loss = LoadDraft<ReportLossDraft>(tempData, assetId, AssetDetailsFormKeys.Loss),
            FormErrorAssign = tempData[ErrorKey(assetId, AssetDetailsFormKeys.Assign)] as string,
            FormErrorReturn = tempData[ErrorKey(assetId, AssetDetailsFormKeys.Return)] as string,
            FormErrorLoss = tempData[ErrorKey(assetId, AssetDetailsFormKeys.Loss)] as string,
            ScrollToForm = tempData[ScrollKey] as string
        };
    }

    public static void ApplyToViewBag(ViewDataDictionary viewData, ITempDataDictionary tempData, Guid assetId)
    {
        var state = Load(tempData, assetId);
        viewData["AssignDraft"] = state.Assign;
        viewData["ReturnDraft"] = state.Return;
        viewData["LossDraft"] = state.Loss;
        viewData["FormErrorAssign"] = state.FormErrorAssign;
        viewData["FormErrorReturn"] = state.FormErrorReturn;
        viewData["FormErrorLoss"] = state.FormErrorLoss;
        viewData["FormScrollTo"] = state.ScrollToForm;
    }

    private static string DraftKey(Guid assetId, string formKey) => $"{DraftKeyPrefix}{assetId}_{formKey}";

    private static string ErrorKey(Guid assetId, string formKey) => $"{ErrorKeyPrefix}{assetId}_{formKey}";

    private static T? LoadDraft<T>(ITempDataDictionary tempData, Guid assetId, string formKey) where T : class
    {
        if (tempData[DraftKey(assetId, formKey)] is not string json || string.IsNullOrWhiteSpace(json))
            return null;
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}

public static class AssetDetailsFormValidation
{
    public const string MissingEmployeeMessage = "اختر موظفاً قبل التسليم";
    public const string MissingProofMessage = "أصل مميز: أرفق ملف المحضر أو اكتب مرجع دليل (رقم محضر / EVD-...) — يكفي أحدهما";
    public const string MissingReturnProofMessage = "أصل مميز: أرفق ملف الاسترداد أو اكتب مرجع دليل — يكفي أحدهما";
    public const string MissingLossProofMessage = "أصل مميز: أرفق ملف دليل الفقدان أو اكتب مرجع دليل — يكفي أحدهما";
    public const string UploadInProgressMessage = "انتظر اكتمال رفع الملف قبل الإرسال";

    public static bool IndicatesUploadInProgress(string? uploadFlag) =>
        string.Equals(uploadFlag, "1", StringComparison.Ordinal) ||
        string.Equals(uploadFlag, "true", StringComparison.OrdinalIgnoreCase);
}
