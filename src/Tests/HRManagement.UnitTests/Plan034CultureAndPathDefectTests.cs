using System.Globalization;
using HR.Core.Infrastructure.Services.Establishment;
using HRManagement.FileStorage;

namespace HRManagement.UnitTests;

/// <summary>
/// QA-034 regression tests for product defects found by the first Linux / ar-SY runs and fixed by owner decision:
/// Q-1 (upload file names keep a Windows client path on Linux) and Q-2 (finance-budget CSV parsed with the request
/// culture). The test assembly runs under ar-SY (TestCulture); the CSV cases also pin it explicitly.
/// </summary>
public class Plan034CultureAndPathDefectTests
{
    [Theory]
    [InlineData(@"C:\fakepath\cv.pdf", "cv.pdf")]
    [InlineData(@"D:\docs\sub\شهادة.pdf", "شهادة.pdf")]
    [InlineData("folder/sub/report.docx", "report.docx")]
    [InlineData(@"..\..\etc\passwd", "passwd")]
    [InlineData("plain.txt", "plain.txt")]
    public void SanitizeFileName_drops_client_directories_on_every_platform(string input, string expected)
    {
        Assert.Equal(expected, FileUploadHelper.SanitizeFileName(input));
    }

    [Fact]
    public void Finance_budget_csv_numbers_are_invariant_under_the_ar_SY_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SY");
        try
        {
            var rows = FileFinanceBudgetPort.ParseCsvContent(
                "CostCenter,GradeCode,ApprovedCount,ApprovedFTE,BudgetAmount\nCC-01,G5,-1,1.5,1000.50\nCC-02,G6,3,2.25,250000\n");

            Assert.Equal(2, rows.Count);
            Assert.Equal(-1, rows[0].ApprovedCount);
            Assert.Equal(1.5m, rows[0].ApprovedFTE);
            Assert.Equal(1000.50m, rows[0].BudgetAmount);
            Assert.Equal(3, rows[1].ApprovedCount);
            Assert.Equal(2.25m, rows[1].ApprovedFTE);
            Assert.Equal(250000m, rows[1].BudgetAmount);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
