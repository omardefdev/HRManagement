namespace HRManagement.FileStorage;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Minio or Local</summary>
    public string Provider { get; set; } = "Minio";

    public string Endpoint { get; set; } = "localhost:9000";

    public bool UseSsl { get; set; }

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Region { get; set; } = "us-east-1";

    /// <summary>Root directory when Provider=Local.</summary>
    public string LocalRootPath { get; set; } = "file-storage";

    public int PresignedUrlExpiryMinutes { get; set; } = 60;

    public int InitRetryCount { get; set; } = 5;

    public int InitRetryDelaySeconds { get; set; } = 3;

    /// <summary>ClamAV clamd host. Empty with ClamAvRequired=false uses EICAR-only path.</summary>
    public string ClamAvHost { get; set; } = string.Empty;

    public int ClamAvPort { get; set; } = 3310;

    public int ClamAvTimeoutSeconds { get; set; } = 30;

    /// <summary>When true (default), ClamAV must succeed; outage → ScanError (fail closed).</summary>
    public bool ClamAvRequired { get; set; } = true;
}
