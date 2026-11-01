using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Testcontainers.RabbitMq;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-38 (§2, AC-OPS-38c/e): the broker image built from docker/rabbitmq/Dockerfile (delayed-message plugin) with the
/// least-privilege users of deploy/bootstrap/rabbitmq-users.json, created through the management API as bootstrap.sh
/// step 6 does. Two real service buses (the shared AddServiceMassTransit) connect with different users: a publish by
/// mq_payroll reaches a consumer of mq_hr_core; mq_payroll cannot declare an hr-core-web queue (ACCESS_REFUSED); a
/// message scheduled 10 s ahead with the delayed scheduler arrives after the delay.
/// </summary>
public sealed class Ops038RabbitLeastPrivilegeFixture : IAsyncLifetime
{
    public const string AdminUser = "ops038_admin";
    public string AdminPassword { get; } = Ops038BootstrapFixture.NewPassword();
    public Dictionary<string, (string User, string Password, string Prefix)> Users { get; } = new(StringComparer.Ordinal);

    private IFutureDockerImage? _image;
    private RabbitMqContainer? _rabbit;

    public RabbitMqContainer Rabbit => _rabbit ?? throw new InvalidOperationException("Container not started");

    public async Task InitializeAsync()
    {
        if (!RealInfra.IsEnabled)
            return;

        var repoRoot = FindRepoRoot();
        var imageName = Environment.GetEnvironmentVariable("HRM_RABBITMQ_IMAGE");
        if (string.IsNullOrWhiteSpace(imageName))
        {
            // The image under test is the repository's own (base pinned by digest, plugin by SHA-256).
            _image = new ImageFromDockerfileBuilder()
                .WithDockerfileDirectory(Path.Combine(repoRoot, "docker", "rabbitmq"))
                .WithDockerfile("Dockerfile")
                .WithName($"hr-rabbitmq-ops038:{Guid.NewGuid():N}")
                .WithCleanUp(true)
                .Build();
            await _image.CreateAsync();
            imageName = _image.FullName;
        }

        _rabbit = new RabbitMqBuilder(imageName)
            .WithUsername(AdminUser)
            .WithPassword(AdminPassword)
            .WithPortBinding(15672, true)
            .Build();
        await _rabbit.StartAsync();

        using var matrix = JsonDocument.Parse(System.IO.File.ReadAllText(
            Path.Combine(repoRoot, "deploy", "bootstrap", "rabbitmq-users.json")));
        using var http = new HttpClient { BaseAddress = new Uri($"http://{_rabbit.Hostname}:{_rabbit.GetMappedPublicPort(15672)}/api/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{AdminUser}:{AdminPassword}")));
        foreach (var u in matrix.RootElement.GetProperty("users").EnumerateArray())
        {
            var service = u.GetProperty("service").GetString()!;
            if (service is not ("payroll-web" or "hr-core-web"))
                continue;
            var user = u.GetProperty("user").GetString()!;
            var password = Ops038BootstrapFixture.NewPassword();
            (await http.PutAsJsonAsync($"users/{user}", new { password, tags = "" })).EnsureSuccessStatusCode();
            var p = u.GetProperty("permissions");
            (await http.PutAsJsonAsync($"permissions/%2F/{user}", new
            {
                configure = p.GetProperty("configure").GetString(),
                write = p.GetProperty("write").GetString(),
                read = p.GetProperty("read").GetString(),
            })).EnsureSuccessStatusCode();
            Users[service] = (user, password, u.GetProperty("endpointPrefix").GetString()!);
        }
    }

    public async Task DisposeAsync()
    {
        if (_rabbit is not null)
            await _rabbit.DisposeAsync();
        if (_image is not null)
            await _image.DisposeAsync();
    }

    public IConfiguration BusConfiguration(string service) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = Rabbit.Hostname,
            ["RabbitMQ:Port"] = Rabbit.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = Users[service].User,
            ["RabbitMQ:Password"] = Users[service].Password,
            ["MassTransit:EndpointPrefix"] = Users[service].Prefix,
            ["MassTransit:RetryCount"] = "0",
        }).Build();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (HRManagement.slnx) not found.");
    }
}

public sealed class Ops038PayrollProcessedProbe : IConsumer<IPayrollProcessed>
{
    public static readonly TaskCompletionSource<Guid> Received = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Consume(ConsumeContext<IPayrollProcessed> context)
    {
        Received.TrySetResult(context.Message.PayrollRunId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Schedules to its own input address from inside a consume, exactly as a saga's <c>Schedule(...)</c> does
/// (HireCaseStateMachine, SeparationStateMachine, TransferSecondmentStateMachines): the delayed exchange
/// <c>&lt;queue&gt;_delay</c> is declared under the service prefix.
/// </summary>
public sealed class Ops038ScheduledProbe : IConsumer<IAgencyOnboarded>
{
    public static readonly TaskCompletionSource<(string AgencyId, DateTime ReceivedUtc)> Received =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task Consume(ConsumeContext<IAgencyOnboarded> context)
    {
        if (context.Message.PackId == "trigger")
        {
            await context.ScheduleSend<IAgencyOnboarded>(context.ReceiveContext.InputAddress, TimeSpan.FromSeconds(10), new
            {
                context.Message.TenantId,
                context.Message.AgencyId,
                PackId = "delayed",
                ReleaseDigest = "ops038",
                OrgUnitCodes = Array.Empty<string>(),
                OnboardedAt = DateTime.UtcNow,
                CorrelationId = (string?)null,
            });
            return;
        }

        Received.TrySetResult((context.Message.AgencyId, DateTime.UtcNow));
    }
}

public sealed class Ops038RabbitLeastPrivilegeTests : IClassFixture<Ops038RabbitLeastPrivilegeFixture>
{
    private const string SkipReason = "Set RUN_TESTCONTAINERS=true and start Docker to run this test.";
    private readonly Ops038RabbitLeastPrivilegeFixture _f;

    public Ops038RabbitLeastPrivilegeTests(Ops038RabbitLeastPrivilegeFixture fixture) => _f = fixture;

    private ServiceProvider Bus(string service, Action<IBusRegistrationConfigurator> consumers)
    {
        var config = _f.BusConfiguration(service);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config);
        services.AddServiceMassTransit(config, consumers, enableInboxFilter: false);
        return services.BuildServiceProvider();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_buses_with_their_own_users_exchange_a_message_and_cannot_touch_each_others_queues()
    {
        Skip.IfNot(RealInfra.IsEnabled, SkipReason);

        await using var hrCore = Bus("hr-core-web", x => x.AddConsumer<Ops038PayrollProcessedProbe>());
        await using var payroll = Bus("payroll-web", _ => { });
        var consumerBus = hrCore.GetRequiredService<IBusControl>();
        var publisherBus = payroll.GetRequiredService<IBusControl>();
        await consumerBus.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
        await publisherBus.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
        try
        {
            var runId = Guid.NewGuid();
            await publisherBus.Publish<IPayrollProcessed>(new
            {
                PayrollRunId = runId,
                PeriodStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc),
                TotalNetPay = 1m,
            });
            Assert.Equal(runId, await Ops038PayrollProcessedProbe.Received.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        finally
        {
            await publisherBus.StopAsync();
            await consumerBus.StopAsync();
        }

        // mq_payroll may not declare a queue of HR.Core (AC-OPS-38c negative).
        var payrollUser = _f.Users["payroll-web"];
        var factory = new ConnectionFactory
        {
            HostName = _f.Rabbit.Hostname,
            Port = _f.Rabbit.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort),
            UserName = payrollUser.User,
            Password = payrollUser.Password,
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var refused = await Assert.ThrowsAsync<OperationInterruptedException>(() =>
            channel.QueueDeclareAsync("hr-core-web-x", durable: true, exclusive: false, autoDelete: false));
        Assert.Equal(403, refused.ShutdownReason!.ReplyCode);
        Assert.Contains("ACCESS_REFUSED", refused.ShutdownReason.ReplyText, StringComparison.Ordinal);

        // ...while its own prefix is allowed.
        await using var own = await connection.CreateChannelAsync();
        var ok = await own.QueueDeclareAsync($"payroll-web-ops038-{Guid.NewGuid():N}", durable: false, exclusive: false, autoDelete: true);
        Assert.StartsWith("payroll-web-", ok.QueueName, StringComparison.Ordinal);

        // Review F2: mq_payroll may not read (get, consume or purge) the HR.Core consumer queue the bus above declared.
        await using var reader = await connection.CreateChannelAsync();
        var readRefused = await Assert.ThrowsAsync<OperationInterruptedException>(() =>
            reader.BasicGetAsync("hr-core-web-ops038-payroll-processed-probe", autoAck: false));
        Assert.Equal(403, readRefused.ShutdownReason!.ReplyCode);
        Assert.Contains("ACCESS_REFUSED", readRefused.ShutdownReason.ReplyText, StringComparison.Ordinal);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_message_scheduled_ten_seconds_ahead_through_the_delayed_exchange_arrives_after_the_delay()
    {
        Skip.IfNot(RealInfra.IsEnabled, SkipReason);

        await using var payroll = Bus("payroll-web", x => x.AddConsumer<Ops038ScheduledProbe>());
        var bus = payroll.GetRequiredService<IBusControl>();
        await bus.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
        try
        {
            var agency = "ops038-" + Guid.NewGuid().ToString("N");
            var sw = Stopwatch.StartNew();
            await bus.Publish<IAgencyOnboarded>(new
            {
                TenantId = Guid.NewGuid(),
                AgencyId = agency,
                PackId = "trigger",
                ReleaseDigest = "ops038",
                OrgUnitCodes = Array.Empty<string>(),
                OnboardedAt = DateTime.UtcNow,
                CorrelationId = (string?)null,
            });

            var received = await Ops038ScheduledProbe.Received.Task.WaitAsync(TimeSpan.FromSeconds(60));
            sw.Stop();
            Assert.Equal(agency, received.AgencyId);
            Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(9), $"arrived after {sw.Elapsed.TotalSeconds:F1} s, before the delay");
        }
        finally
        {
            await bus.StopAsync();
        }
    }
}
