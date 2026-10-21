using HR.Core.Infrastructure;
using HRManagement.SharedKernel.Legal;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

public class Syr001ProductionBindingTests
{
    [Fact]
    public void AddHrLegislationRegister_resolves_provider()
    {
        var services = new ServiceCollection();
        services.AddHrLegislationRegister();
        using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<ILegislationRegisterProvider>();
        Assert.NotNull(provider);
        Assert.IsType<HR.Core.Infrastructure.Services.Legal.LegislationRegisterProvider>(provider);
    }
}
