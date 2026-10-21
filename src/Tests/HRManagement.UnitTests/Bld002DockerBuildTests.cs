using System.Text.RegularExpressions;

namespace HRManagement.UnitTests;

public class Bld002DockerBuildTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "docker-compose.yml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    private static string Read(string relativePath) =>
        System.IO.File.ReadAllText(Path.Combine(ResolveRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Unified_dockerfile_uses_nuget_cache_mount_and_locked_restore()
    {
        var df = Read("docker/DotnetService.Dockerfile");
        Assert.Contains("type=cache,id=hrmanagement-nuget,sharing=locked", df);
        Assert.Contains("--locked-mode", df);
        Assert.DoesNotMatch(new Regex(@"COPY \. \.\s*\r?\n\s*RUN[^\n]*dotnet restore", RegexOptions.Multiline), df);
    }

    [Fact]
    public void Dockerignore_excludes_heavy_paths_and_allows_build_manifest()
    {
        var ignore = Read(".dockerignore");
        foreach (var path in new[] { "deploy/", "terraform/", "perf/", "chaos/", "testdata/" })
            Assert.Contains(path, ignore);
        Assert.Contains("!docker/build-manifest/", ignore);
        Assert.Contains("!docker/DotnetService.Dockerfile", ignore);
    }

    [Fact]
    public void Compose_services_use_unified_dockerfile()
    {
        var compose = Read("docker-compose.yml") + Read("docker-compose.services.yml") + Read("docker-compose.dev-profiles.yml");
        Assert.Contains("docker/DotnetService.Dockerfile", compose);
        Assert.Contains("profiles: [\"full-stack\"]", compose);
        Assert.Contains("dev-logistics", compose);
        Assert.Contains("PROJECT_CSPROJ: src/Services/HRCore/HR.Core.Web/HR.Core.Web.csproj", compose);
        Assert.DoesNotMatch(new Regex(@"dockerfile:\s*src/.+/Dockerfile"), compose);
    }

    [Fact]
    public void Dev_logistics_profile_files_exist()
    {
        var root = ResolveRepoRoot();
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docker-compose.dev-logistics.yml")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "docker-compose.full-stack-gateway.yml")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts/dev-profile-up.ps1")));
        Assert.True(System.IO.File.Exists(Path.Combine(root, "scripts/dev-profile-down.ps1")));
    }

    [Fact]
    public void Service_build_matrix_covers_fifteen_services()
    {
        var json = Read("docker/service-build-matrix.json");
        var matches = Regex.Matches(json, "\"service\":");
        Assert.Equal(15, matches.Count);
    }

    [Fact]
    public void Manifest_generator_script_exists()
    {
        Assert.True(System.IO.File.Exists(Path.Combine(ResolveRepoRoot(), "scripts/docker-generate-build-manifest.ps1")));
        Assert.True(System.IO.File.Exists(Path.Combine(ResolveRepoRoot(), "scripts/bld002-docker-build-validate.ps1")));
    }
}
