namespace HRManagement.Web.Shared.Models;

public sealed class HrFormCardModel
{
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = "fa-edit";
    public string? CancelAction { get; set; }
    public string CancelController { get; set; } = "";
    public string SubmitLabel { get; set; } = "حفظ";
}
