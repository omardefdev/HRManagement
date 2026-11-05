using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>PLAN-42 §3 (G-IAM-13): only the gateway can make a proof, it is bound to the token, and it works once. Keys are made here.</summary>
public class S2s042GatewayProofV2Tests
{
    private const string Path = "/api/employees?page=1";
    private const string TokenA = "Bearer token-of-user-a";
    private const string TokenB = "Bearer token-of-user-b";

    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(60);

    private static IGatewayProofReplayCache Cache() => new GatewayProofReplayCache(new MemoryCache(new MemoryCacheOptions()));

    private static HttpRequestMessage Signed(ECDsa key, string? authorization = TokenA, string path = Path, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, "http://hr-core-web:8080" + path);
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        GatewayCallerProof.SignV2(request, key, path);
        return request;
    }

    private static DefaultHttpContext Received(HttpRequestMessage request, string? authorization = TokenA, string path = Path, string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        var split = path.Split('?', 2);
        context.Request.Path = split[0];
        if (split.Length == 2)
            context.Request.QueryString = new QueryString("?" + split[1]);
        if (authorization is not null)
            context.Request.Headers.Authorization = authorization;
        foreach (var name in new[] { GatewayCallerProof.ProofHeader, GatewayCallerProof.TimestampHeader, GatewayCallerProof.NonceHeader, GatewayCallerProof.PathHeader })
        {
            if (request.Headers.TryGetValues(name, out var values))
                context.Request.Headers[name] = values.Single();
        }

        return context;
    }

    [Fact]
    public void A_valid_signature_passes()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Equal(GatewayProofResult.Valid, GatewayCallerProof.ValidateV2(Received(Signed(key)), key, Cache(), Skew));
    }

    [Fact]
    public void A_request_without_a_token_is_signed_over_the_empty_header()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Equal(GatewayProofResult.Valid, GatewayCallerProof.ValidateV2(Received(Signed(key, null), null), key, Cache(), Skew));
    }

    [Fact]
    public void The_same_proof_works_once()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = Cache();
        var request = Signed(key);

        Assert.Equal(GatewayProofResult.Valid, GatewayCallerProof.ValidateV2(Received(request), key, cache, Skew));
        Assert.Equal(GatewayProofResult.Replay, GatewayCallerProof.ValidateV2(Received(request), key, cache, Skew));
    }

    [Fact]
    public void A_proof_moved_to_another_token_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Equal(GatewayProofResult.BadSignature, GatewayCallerProof.ValidateV2(Received(Signed(key, TokenA), TokenB), key, Cache(), Skew));
        Assert.Equal(GatewayProofResult.BadSignature, GatewayCallerProof.ValidateV2(Received(Signed(key, TokenA), null), key, Cache(), Skew));
    }

    [Fact]
    public void A_proof_for_another_method_or_path_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Equal(GatewayProofResult.BadSignature, GatewayCallerProof.ValidateV2(Received(Signed(key), method: "POST"), key, Cache(), Skew));
        Assert.Equal(GatewayProofResult.PathMismatch, GatewayCallerProof.ValidateV2(Received(Signed(key), path: "/api/payroll?page=1"), key, Cache(), Skew));
    }

    [Fact]
    public void A_changed_path_header_breaks_the_signature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = Received(Signed(key), path: "/api/payroll");
        context.Request.Headers[GatewayCallerProof.PathHeader] = "/api/payroll";

        Assert.Equal(GatewayProofResult.BadSignature, GatewayCallerProof.ValidateV2(context, key, Cache(), Skew));
    }

    [Theory]
    [InlineData(-120)]
    [InlineData(120)]
    public void A_proof_outside_the_skew_is_refused(int seconds)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = Received(Signed(key));
        context.Request.Headers[GatewayCallerProof.TimestampHeader] =
            DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds().ToString();

        Assert.Equal(GatewayProofResult.Skew, GatewayCallerProof.ValidateV2(context, key, Cache(), Skew));
    }

    [Fact]
    public void A_changed_timestamp_inside_the_skew_breaks_the_signature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = Received(Signed(key));
        var sent = long.Parse(context.Request.Headers[GatewayCallerProof.TimestampHeader].ToString());
        context.Request.Headers[GatewayCallerProof.TimestampHeader] = (sent + 5).ToString();

        Assert.Equal(GatewayProofResult.BadSignature, GatewayCallerProof.ValidateV2(context, key, Cache(), Skew));
    }

    [Fact]
    public void A_signature_from_another_key_is_refused_and_leaves_the_nonce_free()
    {
        using var gateway = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = Cache();
        var forged = Signed(attacker);

        Assert.Equal(GatewayProofResult.BadSignature, GatewayCallerProof.ValidateV2(Received(forged), gateway, cache, Skew));

        // A forged proof must not poison the replay cache.
        var nonce = forged.Headers.GetValues(GatewayCallerProof.NonceHeader).Single();
        Assert.True(cache.TryRegister(nonce, Skew));
    }

    [Theory]
    [InlineData(GatewayCallerProof.ProofHeader)]
    [InlineData(GatewayCallerProof.TimestampHeader)]
    [InlineData(GatewayCallerProof.NonceHeader)]
    [InlineData(GatewayCallerProof.PathHeader)]
    public void Every_header_is_required(string header)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = Received(Signed(key));
        context.Request.Headers.Remove(header);

        Assert.Equal(GatewayProofResult.Missing, GatewayCallerProof.ValidateV2(context, key, Cache(), Skew));
    }

    [Theory]
    [InlineData("99999999999999999999")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void A_broken_timestamp_is_refused_without_an_exception(string value)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = Received(Signed(key));
        context.Request.Headers[GatewayCallerProof.TimestampHeader] = value;

        Assert.Equal(GatewayProofResult.Malformed, GatewayCallerProof.ValidateV2(context, key, Cache(), Skew));
    }

    [Fact]
    public void A_broken_signature_is_refused_without_an_exception()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = Received(Signed(key));
        context.Request.Headers[GatewayCallerProof.ProofHeader] = "not base64url !!!";

        Assert.NotEqual(GatewayProofResult.Valid, GatewayCallerProof.ValidateV2(context, key, Cache(), Skew));
    }

    [Fact]
    public void No_public_key_means_no_pass()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Equal(GatewayProofResult.NoKey, GatewayCallerProof.ValidateV2(Received(Signed(key)), null, Cache(), Skew));
    }

    [Fact]
    public void Both_key_formats_load_and_work()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        using var fromPem = GatewayProofKeyLoader.LoadPublic(key.ExportSubjectPublicKeyInfoPem());
        using var fromBase64 = GatewayProofKeyLoader.LoadPublic(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        using var privateFromPem = GatewayProofKeyLoader.LoadPrivate(key.ExportPkcs8PrivateKeyPem());
        using var privateFromBase64 = GatewayProofKeyLoader.LoadPrivate(Convert.ToBase64String(key.ExportPkcs8PrivateKey()));

        Assert.Equal(GatewayProofResult.Valid, GatewayCallerProof.ValidateV2(Received(Signed(privateFromPem)), fromBase64, Cache(), Skew));
        Assert.Equal(GatewayProofResult.Valid, GatewayCallerProof.ValidateV2(Received(Signed(privateFromBase64)), fromPem, Cache(), Skew));
    }

    [Fact]
    public void The_key_script_makes_a_pair_the_application_loads_and_that_signs_and_verifies()
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "proof-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var (exit, stdout, stderr) = HRManagement.TestSupport.RepoScriptRunner.RunScript(
                System.IO.Path.Combine(Hard001Root(), "scripts", "s2s042-new-gateway-proof-keys.ps1"),
                $"-OutFile \"{file}\"",
                120_000);
            Assert.True(exit == 0, stdout + stderr);

            using var keys = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
            using var privateKey = GatewayProofKeyLoader.LoadPrivate(keys.RootElement.GetProperty("privateKeyB64").GetString()!);
            using var publicKey = GatewayProofKeyLoader.LoadPublic(keys.RootElement.GetProperty("publicKeyB64").GetString()!);

            Assert.Equal(GatewayProofResult.Valid, GatewayCallerProof.ValidateV2(Received(Signed(privateKey)), publicKey, Cache(), Skew));
            Assert.DoesNotContain(keys.RootElement.GetProperty("privateKeyB64").GetString()!, stdout + stderr);
        }
        finally
        {
            System.IO.File.Delete(file);
        }
    }

    [Theory]
    [InlineData("USE_ENV_OR_SECRET_MANAGER")]
    [InlineData("not a key")]
    [InlineData("AAAA")]
    public void A_broken_key_is_not_valid(string text)
    {
        Assert.False(GatewayProofKeyLoader.IsValidPublic(text));
        Assert.False(GatewayProofKeyLoader.IsValidPrivate(text));
    }

    [Fact]
    public void Another_curve_and_RSA_are_not_valid()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var rsa = RSA.Create(2048);

        Assert.False(GatewayProofKeyLoader.IsValidPublic(Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo())));
        Assert.False(GatewayProofKeyLoader.IsValidPublic(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())));
    }

    [Fact]
    public void A_public_key_cannot_sign()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var publicOnly = GatewayProofKeyLoader.LoadPublic(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));

        Assert.ThrowsAny<CryptographicException>(() => Signed(publicOnly));
    }

    [Fact]
    public void One_hundred_proofs_sign_and_verify_in_parallel_in_little_time()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var cache = Cache();
        var watch = Stopwatch.StartNew();

        var results = Enumerable.Range(0, 100).AsParallel()
            .Select(_ => GatewayCallerProof.ValidateV2(Received(Signed(key)), key, cache, Skew))
            .ToList();

        watch.Stop();
        Assert.All(results, r => Assert.Equal(GatewayProofResult.Valid, r));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"100 proofs took {watch.Elapsed}");
    }

    private sealed class FixedAuthentication(ClaimsPrincipal? principal) : IAuthenticationService
    {
        public int Calls { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            Calls++;
            return Task.FromResult(principal is null
                ? AuthenticateResult.Fail("invalid token")
                : AuthenticateResult.Success(new AuthenticationTicket(principal, JwtBearerDefaults.AuthenticationScheme)));
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private static async Task<(int Status, bool NextCalled)> Run(
        DefaultHttpContext context,
        ClaimsPrincipal? principal,
        ECDsa? key,
        string environment = "Production",
        Action<GatewayCallerProofOptions>? configure = null,
        IDictionary<string, string?>? settings = null)
    {
        var options = new GatewayCallerProofOptions
        {
            PublicKeyPem = key is null ? null : Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
        };
        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(new FixedAuthentication(principal));
        services.AddSingleton(Mock.Of<IAuthenticationSchemeProvider>(p =>
            p.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme) == Task.FromResult<AuthenticationScheme?>(
                new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)))));
        services.AddSingleton(new GatewayProofKeys(Options.Create(options)));
        services.AddSingleton(Cache());
        context.RequestServices = services.BuildServiceProvider();
        context.Response.Body = new MemoryStream();

        var nextCalled = false;
        var middleware = new GatewayCallerProofMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, NullLogger<GatewayCallerProofMiddleware>.Instance);
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();
        await middleware.InvokeAsync(context, config, Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environment));
        return (context.Response.StatusCode, nextCalled);
    }

    private static ClaimsPrincipal Service() =>
        new(new ClaimsIdentity([new Claim("azp", "svc-admin-logistics")], "Bearer"));

    private static ClaimsPrincipal User(string username = "a-user") =>
        new(new ClaimsIdentity([new Claim("azp", "hr-gateway-client"), new Claim("preferred_username", username)], "Bearer"));

    [Fact]
    public async Task A_service_account_passes_without_a_proof()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/workflow-instances";
        context.Request.Headers.Authorization = "Bearer service-token";

        var (_, next) = await Run(context, Service(), null);

        Assert.True(next);
    }

    [Fact]
    public async Task A_user_without_a_proof_gets_403()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/employees";
        context.Request.Headers.Authorization = TokenA;

        var (status, next) = await Run(context, User(), key);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.False(next);
    }

    [Fact]
    public async Task A_user_named_like_a_service_account_is_still_a_user()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/employees";
        context.Request.Headers.Authorization = TokenA;

        var (status, next) = await Run(context, User("service-account-svc-payroll"), key);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.False(next);
    }

    [Fact]
    public async Task A_token_that_fails_authentication_gets_no_service_pass()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/employees";
        context.Request.Headers.Authorization = "Bearer forged-service-token";

        var (status, _) = await Run(context, null, key);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task A_user_with_a_gateway_proof_passes()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var (_, next) = await Run(Received(Signed(key)), User(), key);

        Assert.True(next);
    }

    private static HttpRequestMessage SignedV1(IConfiguration config)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://hr-core-web:8080" + Path);
        GatewayCallerProof.SignV1(request, config, Path);
        return request;
    }

    private static readonly Dictionary<string, string?> V1Settings = new()
    {
        ["GatewayCallerProof:Secret"] = "unit-test-v1-secret-0123456789abcdef",
        ["GatewayCallerProof:Enforce"] = "true"
    };

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Acceptance")]
    public async Task V1_is_refused_outside_Development_even_with_the_switch_on(string environment)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = new ConfigurationBuilder().AddInMemoryCollection(V1Settings).Build();

        var (status, next) = await Run(Received(SignedV1(config)), User(), key, environment, o => o.AllowV1InDevelopment = true, V1Settings);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.False(next);
    }

    [Fact]
    public async Task V1_in_Development_needs_the_switch()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var config = new ConfigurationBuilder().AddInMemoryCollection(V1Settings).Build();

        var (off, _) = await Run(Received(SignedV1(config)), User(), key, "Development", null, V1Settings);
        var (_, on) = await Run(Received(SignedV1(config)), User(), key, "Development", o => o.AllowV1InDevelopment = true, V1Settings);

        Assert.Equal(StatusCodes.Status403Forbidden, off);
        Assert.True(on);
    }

    [Theory]
    [InlineData("svc-payroll", null, true)]
    [InlineData("hr-gateway-client", "service-account-svc-payroll", false)]
    [InlineData(null, "service-account-svc-payroll", true)]
    [InlineData(null, "a-user", false)]
    public void Azp_decides_who_is_a_service_account(string? azp, string? username, bool expected)
    {
        var claims = new List<Claim>();
        if (azp is not null) claims.Add(new Claim("azp", azp));
        if (username is not null) claims.Add(new Claim("preferred_username", username));

        Assert.Equal(expected, ServiceCallerPrincipal.IsServiceAccount(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))));
    }

    [Fact]
    public async Task The_handler_signs_what_the_gateway_sends_on_its_own_calls()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = new GatewayCallerProofOptions { PrivateKeyPem = Convert.ToBase64String(key.ExportPkcs8PrivateKey()) };
        var handler = new GatewayProofSigningHandler(
            new GatewayProofKeys(Options.Create(options)),
            new ConfigurationBuilder().Build(),
            Mock.Of<IHostEnvironment>(e => e.EnvironmentName == "Production"))
        {
            InnerHandler = new CapturingHandler()
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://hr-core-web:8080/") };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", TokenA);

        await client.GetAsync("api/employees?page=1");

        var sent = ((CapturingHandler)handler.InnerHandler!).Request!;
        Assert.Equal(GatewayProofResult.Valid, GatewayCallerProof.ValidateV2(Received(sent), key, Cache(), Skew));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    [Fact]
    public void No_service_project_signs_a_proof()
    {
        var root = Hard001Root();
        var signers = Directory.GetFiles(System.IO.Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}")
                        && !f.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}")
                        && !f.Contains($"{System.IO.Path.DirectorySeparatorChar}Tests{System.IO.Path.DirectorySeparatorChar}"))
            .Where(f => System.IO.File.ReadAllText(f, Encoding.UTF8).Contains("GatewayCallerProof.Sign", StringComparison.Ordinal)
                        || System.IO.File.ReadAllText(f, Encoding.UTF8).Contains("GatewayProofSigningHandler", StringComparison.Ordinal))
            .Select(f => System.IO.Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("src/Gateway/", StringComparison.Ordinal))
            .ToList();

        // The shared transform and the handler run in the gateway alone; everything else is a service.
        Assert.Equal(
            ["src/Shared/HRManagement.ServiceDefaults/GatewayAccessTokenTransform.cs", "src/Shared/HRManagement.ServiceDefaults/GatewayCallerProofMiddleware.cs"],
            signers.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_image_starts_through_bash_so_that_a_policy_mode_with_a_dot_in_its_name_reaches_the_process()
    {
        var dockerfile = System.IO.File.ReadAllText(System.IO.Path.Combine(Hard001Root(), "docker", "DotnetService.Dockerfile"));

        Assert.Contains("ENTRYPOINT [\"bash\", \"-c\", \"exec dotnet \\\"$APP_DLL\\\"\"]", dockerfile);
    }

    private static string Hard001Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "HRManagement.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
