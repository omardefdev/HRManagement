namespace HR.Core.Web.ViewModels;

public class DashboardViewModel
{
    public int TotalEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public int InactiveEmployees { get; set; }
    public int TotalOrgUnits { get; set; }
    public int TotalQualifications { get; set; }
    public int TotalDocuments { get; set; }

    public Dictionary<string, int> EmployeesByOrgUnit { get; set; } = new();
    public Dictionary<string, int> EmployeesByStatus { get; set; } = new();
    public Dictionary<string, int> EmployeesByGender { get; set; } = new();

    public List<RecentActivityItem> RecentActivities { get; set; } = [];
}

public class RecentActivityItem
{
    public string Description { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Icon { get; set; } = "fas fa-info-circle";
    public string CssClass { get; set; } = "info";
}
