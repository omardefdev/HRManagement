namespace HR.Core.Web.ViewModels;

public class ReportsSummaryViewModel
{
    public int TotalEmployees { get; set; }
    public List<ReportGroupItem> ByStatus { get; set; } = new();
    public List<ReportGroupItem> ByOrgUnit { get; set; } = new();
    public List<ReportGroupItem> ByCadre { get; set; } = new();
    public List<ReportGroupItem> ByGender { get; set; } = new();
}

public class ReportGroupItem
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}
