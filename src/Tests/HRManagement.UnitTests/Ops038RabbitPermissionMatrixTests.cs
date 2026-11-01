using System.Text.Json;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-38 (§2): deploy/bootstrap/rabbitmq-users.json, the least-privilege broker users that hrm-bootstrap creates.
/// Every service that starts a MassTransit bus has exactly one user whose <c>configure</c> expression admits its own
/// endpoint prefix and the shared names it must declare, and refuses every other service's prefix; the production chain
/// connects each service with that user and password variable; every name of the live inventory
/// (reports/plan38-rabbit-topology.json) is admitted for its owner.
/// </summary>
public class Ops038RabbitPermissionMatrixTests
{
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Read(string relative) =>
        System.IO.File.ReadAllText(Path.Combine(ResolveRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private sealed record MqUser(string Service, string EndpointPrefix, string User, string PasswordEnv,
        string Configure, string Write, string ReadPermission);

    private static IReadOnlyList<MqUser> Users()
    {
        using var doc = JsonDocument.Parse(Read("deploy/bootstrap/rabbitmq-users.json"));
        Assert.Equal("/", doc.RootElement.GetProperty("vhost").GetString());
        return doc.RootElement.GetProperty("users").EnumerateArray().Select(u => new MqUser(
            u.GetProperty("service").GetString()!,
            u.GetProperty("endpointPrefix").GetString()!,
            u.GetProperty("user").GetString()!,
            u.GetProperty("passwordEnv").GetString()!,
            u.GetProperty("permissions").GetProperty("configure").GetString()!,
            u.GetProperty("permissions").GetProperty("write").GetString()!,
            u.GetProperty("permissions").GetProperty("read").GetString()!)).ToList();
    }

    /// <summary>The .Web projects that start a bus (AddServiceMassTransit) and the prefix MassTransit gives them.</summary>
    private static IReadOnlyDictionary<string, string> BusServices()
    {
        var root = ResolveRepoRoot();
        var programs = Directory.GetFiles(Path.Combine(root, "src", "Services"), "Program.cs", SearchOption.AllDirectories)
            .Append(Path.Combine(root, "src", "Gateway", "HRManagement.Gateway", "Program.cs"))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var program in programs)
        {
            if (!System.IO.File.ReadAllText(program).Contains("AddServiceMassTransit(", StringComparison.Ordinal))
                continue;
            var project = Path.GetFileName(Path.GetDirectoryName(program)!);
            var prefix = MassTransitExtensions.EndpointPrefixFor(project);
            result[prefix] = project;
        }
        return result;
    }

    private static bool Admits(MqUser user, string name) =>
        Regex.IsMatch(name, user.Configure, RegexOptions.CultureInvariant);

    [Fact]
    public void Every_service_with_a_bus_has_exactly_one_user_and_no_other_user_exists()
    {
        var bus = BusServices();
        var users = Users();

        Assert.Equal(14, bus.Count);
        Assert.Equal(bus.Keys.OrderBy(k => k, StringComparer.Ordinal), users.Select(u => u.EndpointPrefix).OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(users, u => Assert.Equal(u.Service, u.EndpointPrefix));
        Assert.Equal(users.Count, users.Select(u => u.User).Distinct().Count());
        Assert.Equal(users.Count, users.Select(u => u.PasswordEnv).Distinct().Count());
        Assert.All(users, u => Assert.Matches("^mq_[a-z_]+$", u.User));
        Assert.All(users, u => Assert.Matches("^RABBITMQ_[A-Z_]+_PASS$", u.PasswordEnv));
        Assert.DoesNotContain(users, u => u.User == "mq_gateway");
        Assert.All(users, u => Assert.Equal(".*", u.Write));
        Assert.All(users, u => Assert.Equal(".*", u.ReadPermission));
    }

    [Fact]
    public void Each_expression_admits_its_own_names_and_the_shared_ones()
    {
        foreach (var u in Users())
        {
            foreach (var own in new[]
                     {
                         $"{u.EndpointPrefix}-employee-created", $"{u.EndpointPrefix}-employee-created_error",
                         $"{u.EndpointPrefix}-employee-created_skipped", $"{u.EndpointPrefix}-separation-saga-state_delay",
                         "HRManagement.MessageContracts.Events:IEmployeeCreated",
                         "HRManagement.MessageContracts.Commands:IStartHireSaga",
                         "MassTransit:Fault", "MassTransit:Fault--HRManagement.MessageContracts.Events:IEmployeeCreated--",
                         "0123456789ab_dotnet_bus_ybyoyyfqkikqbdbjbdcx46j9yr",
                     })
            {
                Assert.True(Admits(u, own), $"{u.User} must be allowed to declare {own}");
            }
        }

        var contracts = Users().Single(u => u.Service == "hr-contracts-web");
        Assert.True(Admits(contracts, "HR.Contracts.Infrastructure.Sagas:ISeparationSagaFinalized"));
        Assert.All(Users().Where(u => u.Service != "hr-contracts-web"),
            u => Assert.False(Admits(u, "HR.Contracts.Infrastructure.Sagas:ISeparationSagaFinalized"), u.User));
    }

    [Fact]
    public void No_expression_admits_another_services_queue_or_exchange()
    {
        var users = Users();
        foreach (var u in users)
        {
            foreach (var other in users.Where(o => o.Service != u.Service))
            {
                foreach (var name in new[]
                         {
                             $"{other.EndpointPrefix}-x", $"{other.EndpointPrefix}-employee-created",
                             $"{other.EndpointPrefix}-employee-created_error", $"{other.EndpointPrefix}-x_bus_y",
                         })
                {
                    Assert.False(Admits(u, name), $"{u.User} must not be allowed to declare {name}");
                }
            }

            Assert.False(Admits(u, "amq.direct"));
            Assert.False(Admits(u, "unrelated-queue"));
        }
    }

    [Fact]
    public void Every_name_of_the_live_inventory_is_admitted_for_its_owner()
    {
        using var doc = JsonDocument.Parse(Read("reports/plan38-rabbit-topology.json"));
        var users = Users().ToDictionary(u => u.Service, StringComparer.Ordinal);
        var checkedNames = 0;
        foreach (var kind in new[] { "exchanges", "queues" })
        {
            foreach (var entity in doc.RootElement.GetProperty(kind).EnumerateArray())
            {
                var name = entity.GetProperty("name").GetString()!;
                var owner = entity.GetProperty("owner").GetString()!;
                Assert.NotEqual("UNCLASSIFIED", owner);
                if (users.TryGetValue(owner, out var user))
                {
                    Assert.True(Admits(user, name), $"{user.User} must be allowed to declare its {kind[..^1]} {name}");
                    checkedNames++;
                }
                else if (owner == "shared")
                {
                    // A shared message-type or fault exchange: every service bound to it declares it.
                    foreach (var consumer in entity.GetProperty("boundTo").EnumerateArray().Select(e => e.GetString()!))
                        Assert.True(Admits(users[consumer], name), $"{users[consumer].User} must be allowed to declare {name}");
                    checkedNames++;
                }
            }
        }
        Assert.True(checkedNames > 300, $"only {checkedNames} names checked");
    }

    [Fact]
    public void Production_chain_connects_each_service_with_its_matrix_user_and_password_variable()
    {
        var production = Read("docker-compose.production.yml");
        foreach (var u in Users())
        {
            Assert.Contains($"RabbitMQ__Username: {u.User}", production);
            Assert.Contains($"RabbitMQ__Password: ${{{u.PasswordEnv}:?{u.PasswordEnv} required}}", production);
        }
        Assert.DoesNotContain("mq_gateway", production);
        Assert.DoesNotContain("RABBITMQ_SERVICE_PASSWORD", production);
        Assert.DoesNotMatch(@"RabbitMQ__Password: \$\{RABBITMQ_[A-Z_]+_PASS:-", production);

        // The single host runs least-privilege by default (D-38-2): the demo file no longer overrides the broker user,
        // only the explicit admin posture file does.
        Assert.DoesNotContain("RabbitMQ__Username", Read("docker-compose.vps-demo.yml"));
        var admin = Read("docker-compose.posture-mq-admin.yml");
        Assert.Equal(14, Regex.Matches(admin, @"environment: \*mq-admin-env").Count);
        Assert.Contains("RabbitMQ__Username: ${RABBITMQ_DEFAULT_USER:?", admin);
        Assert.Contains("Deployment__MqPosture: admin", admin);
        Assert.Contains("Deployment__MqPosture: least-privilege", production);
        Assert.Contains("docker-compose.posture-mq-admin.yml", Read("deploy/vps/remote-deploy.sh"));
    }
}
