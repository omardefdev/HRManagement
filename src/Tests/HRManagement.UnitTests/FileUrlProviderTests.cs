using HRManagement.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

public class FileUrlProviderTests
{
    private static IFileUrlProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddHrFileUrlProvider(config);
        return services.BuildServiceProvider().GetRequiredService<IFileUrlProvider>();
    }

    [Fact]
    public void Gateway_mode_upload_url_is_site_root_api_files()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["HrAuth:BehindGateway"] = "true"
        });

        Assert.Equal("/api/files", provider.UploadUrl());
    }

    [Fact]
    public void Gateway_mode_download_url_uses_site_root()
    {
        var fileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["HrAuth:BehindGateway"] = "true"
        });

        Assert.Equal($"/api/files/{fileId:D}/download", provider.DownloadUrl(fileId));
        Assert.Equal($"/api/files/{fileId:D}", provider.MetadataUrl(fileId));
        Assert.Equal($"/api/files/{fileId:D}/references", provider.ReferencesUrl(fileId));
    }

    [Fact]
    public void Direct_mode_builds_absolute_urls_from_public_base_url()
    {
        var fileId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["HrAuth:BehindGateway"] = "false",
            ["HrGateway:PublicBaseUrl"] = "http://localhost:5200"
        });

        Assert.Equal("http://localhost:5200/api/files", provider.UploadUrl());
        Assert.Equal($"http://localhost:5200/api/files/{fileId:D}/download", provider.DownloadUrl(fileId));
    }

    [Fact]
    public void Direct_mode_without_public_base_url_throws()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["HrAuth:BehindGateway"] = "false"
        });

        Assert.Throws<InvalidOperationException>(() => provider.UploadUrl());
    }
}
