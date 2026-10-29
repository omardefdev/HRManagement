using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HRManagement.ServiceDefaults.Notifications;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>COM-030 (PLAN-30 F1): channel senders, provider selection and the Production guards.</summary>
public sealed class Hcm030NotificationChannelSenderTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "hcm030-filedrop-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private static IHostEnvironment Env(string name, string? contentRoot = null) =>
        Mock.Of<IHostEnvironment>(e => e.EnvironmentName == name && e.ContentRootPath == (contentRoot ?? Path.GetTempPath()));

    private static IOptions<CandidateNotificationOptions> Opts(Action<CandidateNotificationOptions> configure)
    {
        var options = new CandidateNotificationOptions();
        configure(options);
        return Options.Create(options);
    }

    private static OutboundNotification Message(string key = "e1:Email", string to = "c@test.sy") =>
        new(to, "بوابة التوظيف — تم استلام طلب التوظيف", "تم استلام طلبك بنجاح.", key, "corr-1", "APP_RECEIVED");

    // ---- FileDrop ----

    [Fact]
    public async Task FileDrop_writes_one_utf8_json_file_per_key_and_is_idempotent()
    {
        var sender = new FileDropNotificationSender(
            NotificationChannel.Email, Opts(o => o.FileDrop.Path = folder), Env(Environments.Development));

        var first = await sender.SendAsync(Message("e1:Email"), CancellationToken.None);
        var again = await sender.SendAsync(Message("e1:Email") with { Body = "نص معدّل" }, CancellationToken.None);
        var other = await sender.SendAsync(Message("e2:Email"), CancellationToken.None);

        var name = FileDropNotificationSender.FileNameFor("e1:Email");
        Assert.Equal(PiiMask.Sha256Hex("e1:Email") + ".json", name);
        Assert.Equal(name, Assert.IsType<ChannelSendResult.Sent>(first).ProviderMessageId);
        Assert.IsType<ChannelSendResult.Sent>(again);
        Assert.IsType<ChannelSendResult.Sent>(other);
        var files = Directory.GetFiles(folder);
        Assert.Equal(2, files.Length);
        Assert.DoesNotContain(files, f => f.EndsWith(".tmp", StringComparison.Ordinal));

        var bytes = await System.IO.File.ReadAllBytesAsync(Path.Combine(folder, name));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "no BOM");
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
        var root = doc.RootElement;
        Assert.Equal("APP_RECEIVED", root.GetProperty("template").GetString());
        Assert.Equal("Email", root.GetProperty("channel").GetString());
        Assert.Equal("c@test.sy", root.GetProperty("recipient").GetString());
        Assert.Equal("بوابة التوظيف — تم استلام طلب التوظيف", root.GetProperty("subject").GetString());
        Assert.Equal("نص معدّل", root.GetProperty("body").GetString());
        Assert.Equal("e1:Email", root.GetProperty("idempotencyKey").GetString());
        Assert.Equal("corr-1", root.GetProperty("correlationId").GetString());
        Assert.Equal(DateTimeKind.Utc, root.GetProperty("createdAt").GetDateTime().Kind);
    }

    [Fact]
    public async Task FileDrop_sms_has_no_subject()
    {
        var sender = new FileDropNotificationSender(
            NotificationChannel.Sms, Opts(o => o.FileDrop.Path = folder), Env(Environments.Development));

        await sender.SendAsync(Message("e1:Sms", "+963999000000"), CancellationToken.None);

        using var doc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(Path.Combine(folder, FileDropNotificationSender.FileNameFor("e1:Sms"))));
        Assert.Equal("Sms", doc.RootElement.GetProperty("channel").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("subject").ValueKind);
    }

    [Fact]
    public async Task FileDrop_resolves_a_relative_path_against_the_content_root()
    {
        var sender = new FileDropNotificationSender(
            NotificationChannel.Email, Opts(o => o.FileDrop.Path = "drop"), Env("Acceptance", folder));

        Assert.IsType<ChannelSendResult.Sent>(await sender.SendAsync(Message(), CancellationToken.None));
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "drop")));
    }

    [Fact]
    public async Task FileDrop_refuses_Production_and_writes_nothing()
    {
        var options = Opts(o => { o.Provider = "FileDrop"; o.FileDrop.Path = folder; });
        var sender = new FileDropNotificationSender(NotificationChannel.Email, options, Env(Environments.Production));

        var result = await sender.SendAsync(Message(), CancellationToken.None);

        Assert.IsType<ChannelSendResult.NotConfigured>(result);
        Assert.False(Directory.Exists(folder));
        var validator = new CandidateNotificationStartupValidator(
            options, Env(Environments.Production), NullLogger<CandidateNotificationStartupValidator>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.StartAsync(CancellationToken.None));
        Assert.Null(CandidateNotificationStartupValidator.Evaluate(options.Value, Env("Acceptance")));
        Assert.Null(CandidateNotificationStartupValidator.Evaluate(new CandidateNotificationOptions(), Env(Environments.Production)));
    }

    [Fact]
    public async Task FileDrop_without_a_path_is_not_configured()
    {
        var sender = new FileDropNotificationSender(NotificationChannel.Email, Opts(_ => { }), Env(Environments.Development));

        Assert.IsType<ChannelSendResult.NotConfigured>(await sender.SendAsync(Message(), CancellationToken.None));
        Assert.NotNull(CandidateNotificationStartupValidator.Evaluate(
            new CandidateNotificationOptions { Provider = "FileDrop" }, Env(Environments.Development)));
    }

    // ---- Provider selection ----

    [Theory]
    [InlineData("FileDrop", CandidateNotificationProvider.FileDrop)]
    [InlineData("filedrop", CandidateNotificationProvider.FileDrop)]
    [InlineData("Smtp+Sms", CandidateNotificationProvider.SmtpSms)]
    [InlineData("SmtpSms", CandidateNotificationProvider.SmtpSms)]
    [InlineData("None", CandidateNotificationProvider.None)]
    [InlineData("", CandidateNotificationProvider.None)]
    [InlineData(null, CandidateNotificationProvider.None)]
    [InlineData("Twilio", CandidateNotificationProvider.None)]
    public void Provider_parses_case_insensitively_and_fails_safe(string? value, CandidateNotificationProvider expected) =>
        Assert.Equal(expected, CandidateNotificationOptions.ParseProvider(value));

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("None", new string[0])]
    [InlineData("FileDrop", new[] { "Email:FileDropNotificationSender", "Sms:FileDropNotificationSender" })]
    [InlineData("Smtp+Sms", new[] { "Email:SmtpEmailNotificationSender", "Sms:HttpSmsNotificationSender" })]
    public void Registration_follows_the_provider(string? provider, string[] expected)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Notifications:Candidate:FileDrop:Path"] = folder,
            ["Notifications:Candidate:Sms:Endpoint"] = "https://sms.example.test/send"
        };
        if (provider is not null)
            settings["Notifications:Candidate:Provider"] = provider;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton(Env(Environments.Development));
        services.AddLogging();

        services.AddCandidateNotificationChannels(configuration);
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<NotificationChannelSenderRegistry>();
        var actual = new[] { NotificationChannel.Email, NotificationChannel.Sms }
            .Select(c => registry.For(c) is { } s ? $"{c}:{s.GetType().Name}" : null)
            .OfType<string>()
            .ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(5, registry.MaxAttempts);
        Assert.Contains(sp.GetServices<IHostedService>(), h => h is CandidateNotificationStartupValidator);
    }

    // ---- HTTP SMS ----

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond(request);
        }
    }

    private static (HttpSmsNotificationSender Sender, StubHandler Handler) SmsSender(
        Func<HttpRequestMessage, HttpResponseMessage> respond, Action<SmsChannelOptions>? configure = null)
    {
        var handler = new StubHandler(respond);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(HttpSmsNotificationSender.HttpClientName)).Returns(() => new HttpClient(handler, disposeHandler: false));
        var options = Opts(o =>
        {
            o.Sms.Endpoint = "https://sms.example.test/api/send";
            o.Sms.SenderId = "HR-GOV";
            o.Sms.AuthHeaderName = "X-Api-Key";
            o.Sms.AuthToken = "secret-token";
            configure?.Invoke(o.Sms);
        });
        return (new HttpSmsNotificationSender(factory.Object, options), handler);
    }

    [Fact]
    public async Task Sms_posts_a_json_body_with_the_secret_header_and_no_number_in_the_url()
    {
        var (sender, handler) = SmsSender(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"messageId":"prov-42"}""", Encoding.UTF8, "application/json")
        });

        var result = await sender.SendAsync(
            new OutboundNotification("+963999000000", null, "استلمنا طلبك \"CIT\"", "e1:Sms"), CancellationToken.None);

        Assert.Equal("prov-42", Assert.IsType<ChannelSendResult.Sent>(result).ProviderMessageId);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://sms.example.test/api/send", handler.Request.RequestUri!.ToString());
        Assert.Equal("secret-token", handler.Request.Headers.GetValues("X-Api-Key").Single());
        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal("+963999000000", doc.RootElement.GetProperty("to").GetString());
        Assert.Equal("HR-GOV", doc.RootElement.GetProperty("from").GetString());
        Assert.Equal("استلمنا طلبك \"CIT\"", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal("e1:Sms", doc.RootElement.GetProperty("reference").GetString());
    }

    [Fact]
    public void Sms_body_template_is_filled_by_the_serializer()
    {
        var body = HttpSmsNotificationSender.BuildBody(
            """{"message":{"recipients":["{to}"],"content":"[{sender}] {text}"},"priority":1}""",
            "+963999000000", "HR", "a\"b", "k");

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("+963999000000", doc.RootElement.GetProperty("message").GetProperty("recipients")[0].GetString());
        Assert.Equal("[HR] a\"b", doc.RootElement.GetProperty("message").GetProperty("content").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("priority").GetInt32());
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, typeof(ChannelSendResult.Transient), "HTTP 503")]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(ChannelSendResult.Transient), "HTTP 429")]
    [InlineData(HttpStatusCode.BadRequest, typeof(ChannelSendResult.Permanent), "HTTP 400")]
    [InlineData(HttpStatusCode.Unauthorized, typeof(ChannelSendResult.Permanent), "HTTP 401")]
    public async Task Sms_maps_http_failures(HttpStatusCode status, Type expected, string reason)
    {
        var (sender, _) = SmsSender(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("invalid number +963999000000")
        });

        var result = await sender.SendAsync(new OutboundNotification("+963999000000", null, "x", "k"), CancellationToken.None);

        Assert.IsType(expected, result);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public async Task Sms_transport_errors_are_transient_and_sanitized()
    {
        var (sender, _) = SmsSender(_ => throw new HttpRequestException("connection to +963999000000 failed"));

        var result = await sender.SendAsync(new OutboundNotification("+963999000000", null, "x", "k"), CancellationToken.None);

        Assert.Equal("HttpRequestException", Assert.IsType<ChannelSendResult.Transient>(result).Reason);
    }

    [Fact]
    public async Task Sms_without_endpoint_or_with_a_broken_template_is_not_configured()
    {
        var (noEndpoint, _) = SmsSender(_ => new HttpResponseMessage(HttpStatusCode.OK), o => o.Endpoint = null);
        var (badTemplate, _) = SmsSender(_ => new HttpResponseMessage(HttpStatusCode.OK), o => o.BodyTemplate = "{not json");

        Assert.IsType<ChannelSendResult.NotConfigured>(
            await noEndpoint.SendAsync(new OutboundNotification("+963999000000", null, "x", "k"), CancellationToken.None));
        Assert.IsType<ChannelSendResult.NotConfigured>(
            await badTemplate.SendAsync(new OutboundNotification("+963999000000", null, "x", "k"), CancellationToken.None));
    }

    // ---- SMTP ----

    [Fact]
    public async Task Smtp_without_host_is_not_configured_and_plain_smtp_is_refused_in_Production()
    {
        var none = new SmtpEmailNotificationSender(Opts(_ => { }), Env(Environments.Development));
        var plain = new SmtpEmailNotificationSender(
            Opts(o => { o.Smtp.Host = "127.0.0.1"; o.Smtp.Port = 25; o.Smtp.UseStartTls = false; o.Smtp.From = "no-reply@hr.test"; }),
            Env(Environments.Production));

        Assert.IsType<ChannelSendResult.NotConfigured>(await none.SendAsync(Message(), CancellationToken.None));
        Assert.IsType<ChannelSendResult.NotConfigured>(await plain.SendAsync(Message(), CancellationToken.None));
    }

    [Fact]
    public async Task Smtp_invalid_address_is_permanent()
    {
        var sender = new SmtpEmailNotificationSender(
            Opts(o => { o.Smtp.Host = "127.0.0.1"; o.Smtp.From = "no-reply@hr.test"; }), Env(Environments.Development));

        var result = await sender.SendAsync(Message(to: "not an address"), CancellationToken.None);

        Assert.Equal("Invalid address", Assert.IsType<ChannelSendResult.Permanent>(result).Reason);
    }

    [Fact]
    public async Task Smtp_unreachable_server_is_transient()
    {
        // A port that was free a moment ago: the connection is refused at once.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var sender = new SmtpEmailNotificationSender(
            Opts(o => { o.Smtp.Host = "127.0.0.1"; o.Smtp.Port = port; o.Smtp.UseStartTls = false; o.Smtp.From = "no-reply@hr.test"; o.Smtp.TimeoutSeconds = 5; }),
            Env(Environments.Development));

        var result = await sender.SendAsync(Message(), CancellationToken.None);

        var transient = Assert.IsType<ChannelSendResult.Transient>(result);
        Assert.DoesNotContain("c@test.sy", transient.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Smtp_message_id_is_stable_per_idempotency_key()
    {
        var first = SmtpEmailNotificationSender.BuildMessage("\"بوابة التوظيف\" <no-reply@hr.test>", Message("e9:Email"));
        var second = SmtpEmailNotificationSender.BuildMessage("no-reply@hr.test", Message("e9:Email"));

        Assert.Equal(first.MessageId, second.MessageId);
        Assert.EndsWith("@hr.test", first.MessageId, StringComparison.Ordinal);
        Assert.Equal("auto-generated", first.Headers["Auto-Submitted"]);
        Assert.Equal(MailKit.Security.SecureSocketOptions.StartTls,
            SmtpEmailNotificationSender.SocketOptionsFor(new SmtpChannelOptions()));
        Assert.Equal(MailKit.Security.SecureSocketOptions.SslOnConnect,
            SmtpEmailNotificationSender.SocketOptionsFor(new SmtpChannelOptions { UseStartTls = false, Port = 465 }));
    }
}
