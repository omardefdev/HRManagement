using System.Diagnostics;
using System.Security.Cryptography;
using HRManagement.TestSupport;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-37 (§6, risk "Content via project references does not reach publish"): a real <c>dotnet publish</c> of the two
/// services that did not ship the SYR-003 pack (HR.Contracts, HR.Movements) puts
/// <c>seed-packs/syr003-service-regimes-v1/regimes.json</c> in the publish folder with the repository's SHA-256. The
/// images copy only the publish folder (docker/DotnetService.Dockerfile), so this is what reaches the containers.
/// </summary>
public class Int037PublishOutputTests
{
    public static TheoryData<string> Projects() => new()
    {
        "src/Services/HRContracts/HR.Contracts.Web/HR.Contracts.Web.csproj",
        "src/Services/HRMovements/HR.Movements.Web/HR.Movements.Web.csproj"
    };

    [Theory]
    [MemberData(nameof(Projects))]
    public void Publish_output_contains_the_legal_pack_with_the_repository_fingerprint(string project)
    {
        var outDir = Path.Combine(Path.GetTempPath(), "int037-publish-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A configuration of its own keeps obj/bin of this publish apart from the solution build the tests run from.
            var psi = new ProcessStartInfo("dotnet",
                $"publish \"{Path.Combine(RepoPaths.Root, project)}\" -c Int037Publish -o \"{outDir}\" --nologo -v q -p:UseAppHost=false")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = RepoPaths.Root
            };
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(TimeSpan.FromMinutes(8)), "dotnet publish timed out");
            Assert.True(process.ExitCode == 0, stdout.Result + stderr.Result);

            var shipped = Path.Combine(outDir, "seed-packs", "syr003-service-regimes-v1", "regimes.json");
            Assert.True(System.IO.File.Exists(shipped), $"{project}: {shipped} missing from the publish output");
            var repository = Path.Combine(RepoPaths.Root, "docs", "syr", "service-regimes.json");
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(repository))),
                Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(shipped))));
        }
        finally
        {
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, recursive: true);
        }
    }
}
