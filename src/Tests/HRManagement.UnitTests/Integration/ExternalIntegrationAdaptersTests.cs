using HRManagement.Integration.Abstractions;
using HRManagement.Integration.Abstractions.Identity;
using HRManagement.Integration.Abstractions.Insurance;
using HRManagement.Integration.Abstractions.Payroll;
using HRManagement.Integration.FileAdapters.Identity;
using HRManagement.Integration.FileAdapters.Insurance;
using HRManagement.Integration.FileAdapters.Payroll;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests.Integration;

public class ExternalIntegrationAdaptersTests
{
    [Fact]
    public async Task FilePayrollDisbursementGateway_WritesCsvFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hr-integration-test-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new ExternalIntegrationsOptions { OutputDirectory = dir, Mode = IntegrationMode.File });

        var gateway = new FilePayrollDisbursementGateway(options);
        var request = new PayrollDisbursementRequest(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            "SYP",
            [new PayrollDisbursementLine(Guid.NewGuid(), "01000000001", "موظف تجريبي", 1000m, 100m, 900m)]);

        var result = await gateway.SubmitAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.OutputFilePath);
        Assert.True(System.IO.File.Exists(result.OutputFilePath));
        var content = await System.IO.File.ReadAllTextAsync(result.OutputFilePath!);
        Assert.Contains("EmployeeId", content);
        Assert.Contains("900.00", content);

        try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup best-effort */ }
    }

    [Fact]
    public async Task FileSocialInsuranceReporter_WritesCsvFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hr-integration-test-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new ExternalIntegrationsOptions { OutputDirectory = dir, Mode = IntegrationMode.File });

        var reporter = new FileSocialInsuranceReporter(options);
        var request = new SocialInsuranceReportRequest(
            Guid.NewGuid(),
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            [new SocialInsuranceContributionLine(Guid.NewGuid(), "01000000001", "موظف", 1000m, 70m, 140m)]);

        var result = await reporter.SubmitAsync(request);

        Assert.True(result.Success);
        Assert.True(System.IO.File.Exists(result.OutputFilePath));
    }

    [Fact]
    public async Task StubNationalIdentityVerifier_AcceptsConfiguredPrefix()
    {
        var options = Options.Create(new ExternalIntegrationsOptions { StubNationalIdAcceptedPrefix = "01" });
        var verifier = new StubNationalIdentityVerifier(options);

        var ok = await verifier.VerifyAsync(new NationalIdentityVerificationRequest("01012345678", "اختبار"));
        var fail = await verifier.VerifyAsync(new NationalIdentityVerificationRequest("99012345678"));

        Assert.True(ok.Success);
        Assert.True(ok.Data!.IsValid);
        Assert.True(fail.Success);
        Assert.False(fail.Data!.IsValid);
    }
}
