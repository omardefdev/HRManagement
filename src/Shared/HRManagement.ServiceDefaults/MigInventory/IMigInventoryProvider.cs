namespace HRManagement.ServiceDefaults.MigInventory;

public interface IMigInventoryProvider
{
    MigInventoryOptions Options { get; }
    MigInventoryMatrixDocument Matrix { get; }
}

public interface ILegacyExtractClaimGuard
{
    string? EnsureLiveAgencyExtractAllowed();
    IReadOnlyList<string> ValidateSourceCoverage(MigInventoryMatrixDocument matrix);
    string? ValidateFieldSot(MigFieldSot row, IReadOnlySet<string> sourceIds);
    string? ValidateInventoriedSource(MigSource source);
}
