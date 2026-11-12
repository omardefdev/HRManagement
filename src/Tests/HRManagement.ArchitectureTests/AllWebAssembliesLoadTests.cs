using HRManagement.TestSupport;

namespace HRManagement.ArchitectureTests;

/// <summary>PLAN-51 (§6 rule 1): the catalog lists every web project on disk, and every listed assembly loads; none is skipped.</summary>
public sealed class AllWebAssembliesLoadTests
{
    [Fact]
    public void The_catalog_lists_every_web_project_of_the_repository()
    {
        var onDisk = Directory.GetFiles(Path.Combine(RepoPaths.Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n!.EndsWith(".Web", StringComparison.Ordinal) || n == "HRManagement.Gateway")
            .Select(n => n!)
            .Order(StringComparer.Ordinal)
            .ToList();

        var cataloged = WebAssemblyCatalog.Entries.Where(e => e.IsWebAssembly).Select(e => e.AssemblyName).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(15, cataloged.Count);
        Assert.Equal(onDisk, cataloged);
    }

    public static TheoryData<string> Services() => new(WebAssemblyCatalog.Entries.Select(e => e.Service));

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_cataloged_assembly_loads_with_its_controllers(string service)
    {
        var assembly = WebAssemblyCatalog.AssemblyOf(service);
        Assert.Equal(WebAssemblyCatalog.Entry(service).AssemblyName, assembly.GetName().Name);

        // GetTypes throws on a type that does not load: the test fails, it never skips.
        var controllers = WebAssemblyCatalog.Controllers(service);
        Assert.NotEmpty(controllers);
        Assert.NotEmpty(ActionCatalog.Of(service));
    }
}
