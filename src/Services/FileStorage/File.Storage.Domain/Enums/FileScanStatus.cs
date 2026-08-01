namespace File.Storage.Domain.Enums;

public static class FileScanStatus
{
    public const string PendingScan = "PendingScan";
    public const string Clean = "Clean";
    public const string Infected = "Infected";
    public const string Quarantined = "Quarantined";
    public const string ScanError = "ScanError";
}
