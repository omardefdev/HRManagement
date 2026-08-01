using HRManagement.Gateway.Services;
using Microsoft.Extensions.Configuration;

namespace HRManagement.UnitTests;

public class GatewayModuleCatalogTests
{
    [Fact]
    public void Returns_all_modules_when_feature_flag_list_is_empty()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var catalog = new GatewayModuleCatalog(cfg);

        Assert.True(catalog.Modules.Count >= 13);
        Assert.NotNull(catalog.FindBySlug("hr-core"));
    }

    [Fact]
    public void Filters_modules_when_enabled_modules_is_set()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:EnabledModules:0"] = "hr-core",
                ["Gateway:EnabledModules:1"] = "payroll"
            })
            .Build();

        var catalog = new GatewayModuleCatalog(cfg);

        Assert.Equal(2, catalog.Modules.Count);
        Assert.NotNull(catalog.FindBySlug("hr-core"));
        Assert.NotNull(catalog.FindBySlug("payroll"));
        Assert.Null(catalog.FindBySlug("hr-health"));
    }
}
