namespace HRManagement.UnitTests;

/// <summary>DATA-001: core financial/HR entities must opt into optimistic concurrency in EF models.</summary>
public class Data001ConcurrencyArchitectureTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !global::System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
        }
    }

    [Theory]
    [InlineData("HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs", "Entity<Employee>", "ConfigureOptimisticConcurrency")]
    [InlineData("HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs", "Entity<Contract>", "ConfigureOptimisticConcurrency")]
    [InlineData("HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs", "Entity<AdvanceRequest>", "ConfigureOptimisticConcurrency")]
    [InlineData("Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs", "Entity<PaymentBatch>", "ConfigureOptimisticConcurrency")]
    [InlineData("AdminLogistics/Admin.Logistics.Infrastructure/Data/AdminLogisticsDbContext.cs", "Entity<AssetAssignment>", "ConfigureOptimisticConcurrency")]
    public void DbContext_configures_optimistic_concurrency(string relativePath, string entityMarker, string concurrencyCall)
    {
        var path = Path.Combine(RepoRoot, "src", "Services", relativePath.Replace('/', Path.DirectorySeparatorChar));
        var text = global::System.IO.File.ReadAllText(path);
        var entityIndex = text.IndexOf(entityMarker, StringComparison.Ordinal);
        Assert.True(entityIndex >= 0, $"Missing {entityMarker} in {relativePath}");
        var nextEntity = text.IndexOf("Entity<", entityIndex + entityMarker.Length, StringComparison.Ordinal);
        var blockEnd = nextEntity > 0 ? nextEntity : text.Length;
        var block = text[entityIndex..blockEnd];
        Assert.Contains(concurrencyCall, block, StringComparison.Ordinal);
    }
}
