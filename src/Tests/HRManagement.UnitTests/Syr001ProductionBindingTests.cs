using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

public class Syr001ProductionBindingTests
{
    [Fact]
    public void AddHrLegislationRegister_resolves_provider()
    {
        // QA-034: LegislationRegisterProvider reads HRCoreDbContext, as in HR.Core.Web (Program.cs registers the
        // context before AddHrLegislationRegister). ValidateOnBuild fails here if the register gains a dependency
        // the host does not provide.
        var services = new ServiceCollection();
        services.AddDbContext<HRCoreDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        services.AddHrLegislationRegister();
        using var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = sp.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<ILegislationRegisterProvider>();
        Assert.NotNull(provider);
        Assert.IsType<HR.Core.Infrastructure.Services.Legal.LegislationRegisterProvider>(provider);
    }
}
