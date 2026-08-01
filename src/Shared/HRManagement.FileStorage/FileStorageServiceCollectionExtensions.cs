using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;

namespace HRManagement.FileStorage;

public static class FileStorageServiceCollectionExtensions
{
    public static IServiceCollection AddHrFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));

        var provider = configuration.GetSection(FileStorageOptions.SectionName).GetValue<string>("Provider") ?? "Minio";
        if (string.Equals(provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        }
        else
        {
            services.AddSingleton<IMinioClient>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<FileStorageOptions>>().Value;
                var builder = new MinioClient()
                    .WithEndpoint(options.Endpoint)
                    .WithCredentials(options.AccessKey, options.SecretKey)
                    .WithRegion(options.Region);
                if (options.UseSsl)
                    builder = builder.WithSSL();
                return builder.Build();
            });
            services.AddSingleton<IFileStorageService, MinioFileStorageService>();
        }

        services.AddHostedService<FileStorageInitializer>();
        services.AddSingleton<EicarAwareScanner>();
        services.AddSingleton<ClamAvMalwareScanner>();
        services.AddSingleton<IMalwareScanner, CompositeMalwareScanner>();
        services.AddSingleton<IContentDisarmService, ContentDisarmService>();
        return services;
    }
}

internal sealed class FileStorageInitializer(
    IFileStorageService storage,
    IOptions<FileStorageOptions> options,
    ILogger<FileStorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var retryCount = Math.Max(1, options.Value.InitRetryCount);
        var delay = TimeSpan.FromSeconds(Math.Max(1, options.Value.InitRetryDelaySeconds));

        for (var attempt = 1; attempt <= retryCount; attempt++)
        {
            try
            {
                await storage.EnsureBucketsAsync(cancellationToken);
                logger.LogInformation("File storage buckets initialized.");
                return;
            }
            catch (Exception ex) when (attempt < retryCount)
            {
                logger.LogWarning(ex, "File storage init attempt {Attempt}/{Total} failed; retrying...", attempt, retryCount);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
