using HRManagement.SharedKernel.Exports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HRManagement.ServiceDefaults;

/// <summary>PERF-004: local streaming artifact store (disk). Compatible with retention via ExpiresAt on ExportJob.</summary>
public interface IExportArtifactStore
{
    Task<ExportArtifactRef> SaveAsync(
        Guid tenantId,
        Guid jobId,
        string fileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(ExportArtifactRef reference, CancellationToken cancellationToken = default);

    Task DeleteAsync(ExportArtifactRef reference, CancellationToken cancellationToken = default);
}

public sealed record ExportArtifactRef(Guid FileId, string RelativePath, string ContentType, string FileName);

public sealed class LocalExportArtifactStore(IHostEnvironment env) : IExportArtifactStore
{
    public async Task<ExportArtifactRef> SaveAsync(
        Guid tenantId,
        Guid jobId,
        string fileName,
        string contentType,
        Stream content,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        var fileId = Guid.NewGuid();
        var safeName = Path.GetFileName(fileName);
        var relative = Path.Combine("export-artifacts", tenantId.ToString("N"), jobId.ToString("N"), $"{fileId:N}_{safeName}");
        var full = Path.Combine(env.ContentRootPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        await using var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
        await content.CopyToAsync(fs, cancellationToken);
        await fs.FlushAsync(cancellationToken);

        return new ExportArtifactRef(fileId, relative.Replace('\\', '/'), contentType, safeName);
    }

    public Task<Stream> OpenReadAsync(ExportArtifactRef reference, CancellationToken cancellationToken = default)
    {
        var full = Path.Combine(env.ContentRootPath, reference.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
            throw new FileNotFoundException("Export artifact missing.", full);
        Stream stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(ExportArtifactRef reference, CancellationToken cancellationToken = default)
    {
        var full = Path.Combine(env.ContentRootPath, reference.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full))
            File.Delete(full);
        return Task.CompletedTask;
    }
}

public static class ExportJobServiceCollectionExtensions
{
    public static IServiceCollection AddExportJobInfrastructure(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<ExportJobOptions>(configuration.GetSection(ExportJobOptions.SectionName));
        services.AddSingleton<IExportArtifactStore, LocalExportArtifactStore>();
        return services;
    }
}
