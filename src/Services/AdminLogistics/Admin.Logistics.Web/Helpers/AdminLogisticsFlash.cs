using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Admin.Logistics.Web.Helpers;

internal static class AdminLogisticsFlash
{
    public static void Error(ITempDataDictionary tempData, string message, bool dialog = true)
    {
        tempData["Error"] = message;
        if (dialog)
            tempData["ShowDialog"] = "true";
    }

    public static void Success(ITempDataDictionary tempData, string message) =>
        tempData["Success"] = message;

    public static void Info(ITempDataDictionary tempData, string message) =>
        tempData["Info"] = message;
}
