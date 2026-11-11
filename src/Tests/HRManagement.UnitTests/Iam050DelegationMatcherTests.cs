using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Auth.Directory.Infrastructure.Keycloak;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-50 (§5): the matcher, the creation rules, the role-or-delegation requirement (with and without a lookup), the HTTP lookup's cache
/// and fail-closed answers, the acting context, and the provisioning payload's <c>employee_id</c>.
/// </summary>
public class Iam050DelegationMatcherTests
{
    private const string Approver = HrManagementRoles.HrManagementApprover;
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Principal = Guid.Parse("50000000-0000-4000-8000-000000000001");
    private static readonly Guid DelegateUser = Guid.Parse("50000000-0000-4000-8000-000000000002");
    private static readonly Guid DelegateEmployee = Guid.Parse("50000000-0000-4000-8000-0000000000e2");

    private static ApprovalDelegation Row(
        string? role = Approver,
        string? entityType = null,
        Guid? delegateEmployee = null,
        Guid? delegateUser = null,
        DateTime? from = null,
        DateTime? until = null,
        bool revoked = false) => new()
        {
            PrincipalId = Principal,
            DelegateEmployeeId = delegateEmployee ?? DelegateEmployee,
            DelegateId = delegateUser,
            RoleScope = role,
            EntityTypeScope = entityType,
            ActiveFrom = from ?? Now.AddDays(-1),
            ActiveUntil = until ?? Now.AddDays(6),
            Reason = "إجازة",
            IsRevoked = revoked
        };

    // ---- the matcher ----

    [Fact]
    public void A_delegation_in_force_matches_its_delegate_role_and_type()
    {
        var row = Row(entityType: "TransferRequest");
        Assert.Same(row, ApprovalDelegationMatcher.Match([row], null, DelegateEmployee, Approver, "TransferRequest", Now));
        Assert.Same(row, ApprovalDelegationMatcher.Match([row], null, DelegateEmployee, "HR-MANAGEMENT-APPROVER", "transferrequest", Now));
    }

    [Fact]
    public void Each_condition_alone_refuses()
    {
        Assert.Null(ApprovalDelegationMatcher.Match([Row()], null, Guid.NewGuid(), Approver, "TransferRequest", Now)); // another employee
        Assert.Null(ApprovalDelegationMatcher.Match([Row()], DelegateUser, null, Approver, "TransferRequest", Now)); // user id of a row that names an employee
        Assert.Null(ApprovalDelegationMatcher.Match([Row()], null, DelegateEmployee, HrManagementRoles.HrCore, "TransferRequest", Now)); // another role
        Assert.Null(ApprovalDelegationMatcher.Match([Row(entityType: "RecruitmentRequisition")], null, DelegateEmployee, Approver, "TransferRequest", Now)); // another type
        Assert.Null(ApprovalDelegationMatcher.Match([Row(entityType: "RecruitmentRequisition")], null, DelegateEmployee, Approver, null, Now)); // unknown type, scoped row
        Assert.Null(ApprovalDelegationMatcher.Match([Row(revoked: true)], null, DelegateEmployee, Approver, "TransferRequest", Now)); // revoked
        Assert.Null(ApprovalDelegationMatcher.Match([Row(until: Now.AddSeconds(-1))], null, DelegateEmployee, Approver, "TransferRequest", Now)); // expired
        Assert.Null(ApprovalDelegationMatcher.Match([Row(from: Now.AddSeconds(1))], null, DelegateEmployee, Approver, "TransferRequest", Now)); // not started
        Assert.Null(ApprovalDelegationMatcher.Match([Row(role: null)], null, DelegateEmployee, Approver, "TransferRequest", Now)); // no role (D-50-4)
        Assert.Null(ApprovalDelegationMatcher.Match([Row(role: "  ")], null, DelegateEmployee, Approver, "TransferRequest", Now));
    }

    [Fact]
    public void The_period_bounds_are_inclusive()
    {
        Assert.NotNull(ApprovalDelegationMatcher.Match([Row(from: Now)], null, DelegateEmployee, Approver, null, Now));
        Assert.NotNull(ApprovalDelegationMatcher.Match([Row(until: Now)], null, DelegateEmployee, Approver, null, Now));
    }

    [Fact]
    public void A_row_older_than_plan_50_matches_by_user()
    {
        var old = new ApprovalDelegation
        {
            PrincipalId = Principal,
            DelegateId = DelegateUser,
            RoleScope = Approver,
            ActiveFrom = Now.AddDays(-1),
            ActiveUntil = Now.AddDays(1),
            Reason = "قديم"
        };
        Assert.Same(old, ApprovalDelegationMatcher.Match([old], DelegateUser, null, Approver, "TransferRequest", Now));
        Assert.Null(ApprovalDelegationMatcher.Match([old], Guid.NewGuid(), DelegateEmployee, Approver, "TransferRequest", Now));
    }

    [Fact]
    public void The_one_that_ends_last_wins_whatever_the_order()
    {
        var shorter = Row(until: Now.AddDays(2));
        var longer = Row(until: Now.AddDays(5));
        Assert.Same(longer, ApprovalDelegationMatcher.Match([shorter, longer], null, DelegateEmployee, Approver, null, Now));
        Assert.Same(longer, ApprovalDelegationMatcher.Match([longer, shorter], null, DelegateEmployee, Approver, null, Now));
    }

    [Fact]
    public void The_lookup_form_rechecks_role_type_and_the_end_of_a_cached_answer()
    {
        var active = ApprovalDelegationMatcher.ToActive(Row(entityType: "TransferRequest", until: Now.AddMinutes(1)));
        Assert.Equal(active, ApprovalDelegationMatcher.Match([active], Approver, "TransferRequest", Now));
        Assert.Null(ApprovalDelegationMatcher.Match([active], Approver, "PromotionRequest", Now));
        Assert.Null(ApprovalDelegationMatcher.Match([active], Approver, "TransferRequest", Now.AddMinutes(2)));
        Assert.Equal(active, ApprovalDelegationMatcher.MatchRole([active], Approver, Now));
        Assert.Null(ApprovalDelegationMatcher.MatchRole([active], HrManagementRoles.HrCore, Now));
    }

    [Fact]
    public void ActiveFor_returns_only_the_actors_delegations_in_force()
    {
        var mine = Row();
        var other = Row(delegateEmployee: Guid.NewGuid());
        var revoked = Row(revoked: true);
        var list = ApprovalDelegationMatcher.ActiveFor([mine, other, revoked], null, DelegateEmployee, Now);
        var single = Assert.Single(list);
        Assert.Equal(mine.Id, single.Id);
        Assert.Equal(Principal, single.PrincipalUserId);
    }

    // ---- creation (D-WF-5, D-50-1) ----

    private static readonly DelegationRules Rules = new(
        DelegationOptions.DefaultDelegableRoles, DelegationOptions.DefaultEntityTypes, [Approver, HrManagementRoles.HrCore]);

    private static ApprovalDelegation Proposed(Action<ApprovalDelegation>? change = null)
    {
        var d = new ApprovalDelegation
        {
            PrincipalId = Principal,
            PrincipalEmployeeId = Guid.Parse("50000000-0000-4000-8000-0000000000e1"),
            DelegateEmployeeId = DelegateEmployee,
            RoleScope = Approver,
            EntityTypeScope = "RecruitmentRequisition",
            ActiveFrom = Now,
            ActiveUntil = Now.AddDays(7),
            Reason = "سفر"
        };
        change?.Invoke(d);
        return d;
    }

    [Fact]
    public void A_valid_delegation_is_allowed() =>
        Assert.True(SeparationOfDutiesService.ValidateDelegation(Proposed(), Rules).Allowed);

    public static TheoryData<string, string> Refusals => new()
    {
        { "no-role", "يجب تحديد الدور المفوَّض." },
        { "not-delegable", "الدور «hr-core» غير قابل للتفويض." },
        { "not-held", "لا يمكن تفويض الدور «hr-management-approver» لأنك لا تحمله مباشرة." },
        { "type-outside-list", "نوع الكيان «LeaveRequest» ليس من الأنواع التي يجوز التفويض فيها." },
        { "self-by-employee", "لا يمكن التفويض للنفس." },
        { "self-by-user", "لا يمكن التفويض للنفس." },
        { "no-delegate", "يجب اختيار المفوَّض من دليل الموظفين." },
        { "91-days", "لا يجوز أن تتجاوز مدة التفويض 90 يوماً." },
        { "empty-period", "فترة التفويض غير صالحة." },
        { "no-reason", "سبب التفويض مطلوب." },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Each_rule_refuses_with_its_arabic_message(string @case, string message)
    {
        var rules = Rules;
        var d = @case switch
        {
            "no-role" => Proposed(x => x.RoleScope = null),
            "not-delegable" => Proposed(x => x.RoleScope = HrManagementRoles.HrCore),
            "not-held" => Proposed(),
            "type-outside-list" => Proposed(x => x.EntityTypeScope = "LeaveRequest"),
            "self-by-employee" => Proposed(x => x.DelegateEmployeeId = x.PrincipalEmployeeId),
            "self-by-user" => Proposed(x => { x.DelegateEmployeeId = null; x.DelegateId = Principal; }),
            "no-delegate" => Proposed(x => x.DelegateEmployeeId = null),
            "91-days" => Proposed(x => x.ActiveUntil = x.ActiveFrom.AddDays(91)),
            "empty-period" => Proposed(x => x.ActiveUntil = x.ActiveFrom),
            "no-reason" => Proposed(x => x.Reason = " "),
            _ => throw new ArgumentOutOfRangeException(nameof(@case))
        };
        if (@case == "not-held")
            rules = Rules with { PrincipalDirectRoles = [HrManagementRoles.HrCore] }; // the delegate of a delegation holds no delegated role

        var result = SeparationOfDutiesService.ValidateDelegation(d, rules);
        Assert.False(result.Allowed);
        Assert.Equal(message, result.DenialReason);
    }

    [Fact]
    public void Ninety_days_exactly_is_allowed() =>
        Assert.True(SeparationOfDutiesService.ValidateDelegation(Proposed(x => x.ActiveUntil = x.ActiveFrom.AddDays(90)), Rules).Allowed);

    // ---- review 2: the bound from the creation instant ----

    [Fact]
    public void A_window_set_far_ahead_is_refused_from_the_creation_instant()
    {
        // Each window alone is 30 days, but it starts 80 days after its creation: chained, such windows would cover far more than 90 days.
        var ahead = Proposed(x => { x.ActiveFrom = Now.AddDays(80); x.ActiveUntil = Now.AddDays(110); });
        Assert.True(SeparationOfDutiesService.ValidateDelegation(ahead, Rules).Allowed); // without the creation instant (old callers)

        var result = SeparationOfDutiesService.ValidateDelegation(ahead, Rules with { CreatedAt = Now });
        Assert.False(result.Allowed);
        Assert.Equal("لا يجوز أن ينتهي التفويض بعد أكثر من 90 يوماً من إنشائه.", result.DenialReason);
    }

    [Fact]
    public void A_window_that_ended_before_its_creation_is_refused()
    {
        var past = Proposed(x => { x.ActiveFrom = Now.AddDays(-10); x.ActiveUntil = Now.AddDays(-1); });
        var result = SeparationOfDutiesService.ValidateDelegation(past, Rules with { CreatedAt = Now });
        Assert.False(result.Allowed);
        Assert.Equal("تنتهي فترة التفويض قبل إنشائه.", result.DenialReason);
    }

    [Fact]
    public void Ninety_days_from_the_creation_instant_is_allowed()
    {
        Assert.True(SeparationOfDutiesService.ValidateDelegation(
            Proposed(x => { x.ActiveFrom = Now.AddHours(-2); x.ActiveUntil = Now.AddDays(89).AddHours(22); }), Rules with { CreatedAt = Now }).Allowed);
        Assert.True(SeparationOfDutiesService.ValidateDelegation(
            Proposed(x => { x.ActiveFrom = Now; x.ActiveUntil = Now.AddDays(90); }), Rules with { CreatedAt = Now }).Allowed);
    }

    // ---- options ----

    [Fact]
    public void Missing_or_empty_settings_fall_back_to_the_narrow_defaults()
    {
        Assert.Equal([Approver], DelegationOptions.DelegableRoles(null));
        Assert.Equal(TimeSpan.FromSeconds(60), DelegationOptions.CacheDuration(null));
        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Delegation:DelegableRoles:0"] = " ",
            ["Delegation:CacheSeconds"] = "9999"
        }).Build();
        Assert.Equal([Approver], DelegationOptions.DelegableRoles(configured));
        Assert.Equal(TimeSpan.FromSeconds(300), DelegationOptions.CacheDuration(configured));
        Assert.Contains("TransferRequest", DelegationOptions.EntityTypes(configured));
    }

    // ---- the requirement ----

    private sealed class FakeLookup(Func<IReadOnlyList<ActiveDelegation>> answer) : IApprovalDelegationLookup
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ActiveDelegation>> GetActiveForAsync(ClaimsPrincipal user, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(answer());
        }
    }

    private static ClaimsPrincipal Person(params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, DelegateUser.ToString("D")),
            new("employee_id", DelegateEmployee.ToString("D")),
            new("azp", "hr-gateway-client"),
            new(TenantContext.TenantClaimType, Guid.NewGuid().ToString("D"))
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ActiveDelegation Active(string? entityType = null, DateTime? until = null) =>
        new(Guid.NewGuid(), Principal, null, "الأصيل", Approver, entityType, until ?? DateTime.UtcNow.AddDays(1));

    private static (IAuthorizationService Authorization, HttpContext Http) Arrange(IApprovalDelegationLookup? lookup)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAuthorization(o =>
        {
            o.AddPolicy("approve", p => p.AddRequirements(new RoleOrDelegationRequirement([HrManagementRoles.Admin, Approver], Approver)));
            o.AddPolicy("step", p => p.AddRequirements(new RoleOrDelegationRequirement([HrManagementRoles.HrCore], null, anyDelegableRole: true)));
        });
        services.AddSingleton<IAuthorizationHandler, RoleOrDelegationHandler>();
        if (lookup is not null)
            services.AddSingleton(lookup);
        var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        return (provider.GetRequiredService<IAuthorizationService>(), http);
    }

    [Fact]
    public async Task With_no_lookup_registered_it_is_the_roles_only()
    {
        var (authorization, http) = Arrange(null);
        Assert.True((await authorization.AuthorizeAsync(Person(Approver), http, "approve")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Person(HrManagementRoles.HrCore), http, "approve")).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(Person(HrManagementRoles.HrCore), new ApprovalEntityTypeResource("TransferRequest"), "approve")).Succeeded);
    }

    [Fact]
    public async Task A_delegate_passes_for_the_delegated_role_and_type_and_the_match_is_kept_for_the_request()
    {
        var delegation = Active("TransferRequest");
        var (authorization, http) = Arrange(new FakeLookup(() => [delegation]));
        Assert.True((await authorization.AuthorizeAsync(Person(HrManagementRoles.HrCore), http, "approve")).Succeeded);
        Assert.Equal(delegation, http.Items[ApprovalActingContext.ItemKey]);

        var (authorization2, http2) = Arrange(new FakeLookup(() => [delegation]));
        Assert.False((await authorization2.AuthorizeAsync(Person(HrManagementRoles.HrCore), new ApprovalEntityTypeResource("PromotionRequest"), "approve")).Succeeded);
        Assert.False(http2.Items.ContainsKey(ApprovalActingContext.ItemKey));
    }

    [Fact]
    public async Task The_step_policy_passes_a_delegate_of_any_delegable_role_and_type_and_lets_the_evaluator_decide()
    {
        var (authorization, http) = Arrange(new FakeLookup(() => [Active("RecruitmentRequisition")]));
        Assert.True((await authorization.AuthorizeAsync(Person(HrManagementRoles.HrCoreSupervisory), http, "step")).Succeeded);
    }

    [Fact]
    public async Task An_expired_answer_a_failing_lookup_and_a_service_account_get_nothing()
    {
        var (a1, h1) = Arrange(new FakeLookup(() => [Active(until: DateTime.UtcNow.AddSeconds(-1))]));
        Assert.False((await a1.AuthorizeAsync(Person(), h1, "approve")).Succeeded);

        var (a2, h2) = Arrange(new FakeLookup(() => throw new HttpRequestException("down")));
        Assert.False((await a2.AuthorizeAsync(Person(), h2, "approve")).Succeeded);

        var lookup = new FakeLookup(() => [Active()]);
        var (a3, h3) = Arrange(lookup);
        var service = new ClaimsPrincipal(new ClaimsIdentity([new Claim("azp", ServiceClientIds.HrMovements)], "Test"));
        Assert.False((await a3.AuthorizeAsync(service, h3, "approve")).Succeeded);
        Assert.Equal(0, lookup.Calls);
    }

    [Fact]
    public async Task A_holder_of_the_direct_role_never_asks_the_lookup()
    {
        var lookup = new FakeLookup(() => throw new InvalidOperationException("must not be called"));
        var (authorization, http) = Arrange(lookup);
        Assert.True((await authorization.AuthorizeAsync(Person(Approver), http, "approve")).Succeeded);
        Assert.Equal(0, lookup.Calls);
    }

    // ---- the acting context ----

    private static ApprovalActingContext Context(ClaimsPrincipal user, IApprovalDelegationLookup? lookup)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } };
        return new ApprovalActingContext(accessor, NullLogger<ApprovalActingContext>.Instance, lookup);
    }

    [Fact]
    public async Task The_direct_role_wins_over_a_delegation()
    {
        var acting = await Context(Person(Approver), new FakeLookup(() => [Active()]))
            .GetAsync([HrManagementRoles.Admin, Approver], Approver, "TransferRequest");
        Assert.True(acting.HoldsDirectRole);
        Assert.Null(acting.OnBehalfOf);
        Assert.Equal(DelegateUser, acting.UserId);
        Assert.Equal(DelegateEmployee, acting.EmployeeId);
    }

    [Fact]
    public async Task A_delegate_acts_on_behalf_and_a_failed_lookup_acts_for_nobody()
    {
        var delegation = Active("TransferRequest");
        var acting = await Context(Person(HrManagementRoles.HrCore), new FakeLookup(() => [delegation]))
            .GetAsync([HrManagementRoles.Admin, Approver], Approver, "TransferRequest");
        Assert.False(acting.HoldsDirectRole);
        Assert.Equal(delegation, acting.OnBehalfOf);
        Assert.True(acting.MayAct);

        var failed = await Context(Person(HrManagementRoles.HrCore), new FakeLookup(() => throw new HttpRequestException("down")))
            .GetAsync([HrManagementRoles.Admin, Approver], Approver, "TransferRequest");
        Assert.False(failed.MayAct);

        var wrongType = await Context(Person(HrManagementRoles.HrCore), new FakeLookup(() => [delegation]))
            .GetAsync([HrManagementRoles.Admin, Approver], Approver, "PromotionRequest");
        Assert.False(wrongType.MayAct);
    }

    // ---- the HTTP lookup ----

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://hr-core-web:8080/") };
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task The_http_lookup_sends_the_identity_and_the_tenant_and_keeps_the_answer()
    {
        var tenant = Guid.NewGuid();
        var delegation = Active("TransferRequest");
        var handler = new StubHandler(_ => Json(new ApprovalDelegationsResponse([delegation])));
        var lookup = new HttpApprovalDelegationLookup(new StubFactory(handler), new MemoryCache(new MemoryCacheOptions()),
            new ConfigurationBuilder().Build(), NullLogger<HttpApprovalDelegationLookup>.Instance);
        var previous = TenantIdFlow.Get();
        TenantIdFlow.Set(tenant);
        try
        {
            var first = await lookup.GetActiveForAsync(Person(HrManagementRoles.HrCore));
            var second = await lookup.GetActiveForAsync(Person(HrManagementRoles.HrCore));
            Assert.Equal(delegation.Id, Assert.Single(first).Id);
            Assert.Equal(delegation.Id, Assert.Single(second).Id);
            var request = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains($"userId={DelegateUser:D}", request.RequestUri!.Query, StringComparison.Ordinal);
            Assert.Contains($"employeeId={DelegateEmployee:D}", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Equal("/" + HttpApprovalDelegationLookup.ActivePath, request.RequestUri.AbsolutePath);
            Assert.Equal(tenant.ToString("D"), request.Headers.GetValues(TenantIdMiddleware.HeaderName).Single());
        }
        finally
        {
            TenantIdFlow.Set(previous);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_refusal_or_an_error_means_no_delegation_and_is_not_kept(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));
        var lookup = new HttpApprovalDelegationLookup(new StubFactory(handler), new MemoryCache(new MemoryCacheOptions()),
            new ConfigurationBuilder().Build(), NullLogger<HttpApprovalDelegationLookup>.Instance);
        Assert.Empty(await lookup.GetActiveForAsync(Person()));
        Assert.Empty(await lookup.GetActiveForAsync(Person()));
        Assert.Equal(2, handler.Requests.Count);

        var throwing = new HttpApprovalDelegationLookup(new StubFactory(new StubHandler(_ => throw new HttpRequestException("down"))),
            new MemoryCache(new MemoryCacheOptions()), new ConfigurationBuilder().Build(), NullLogger<HttpApprovalDelegationLookup>.Instance);
        Assert.Empty(await throwing.GetActiveForAsync(Person()));
    }

    [Fact]
    public async Task No_identity_or_no_tenant_asks_nothing()
    {
        var handler = new StubHandler(_ => Json(new ApprovalDelegationsResponse([Active()])));
        var lookup = new HttpApprovalDelegationLookup(new StubFactory(handler), new MemoryCache(new MemoryCacheOptions()),
            new ConfigurationBuilder().Build(), NullLogger<HttpApprovalDelegationLookup>.Instance);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(TenantContext.TenantClaimType, Guid.NewGuid().ToString("D"))], "Test"));
        Assert.Empty(await lookup.GetActiveForAsync(anonymous));
        var noTenant = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, DelegateUser.ToString("D"))], "Test"));
        Assert.Empty(await lookup.GetActiveForAsync(noTenant));
        Assert.Empty(handler.Requests);
    }

    // ---- Auth.Directory ----

    [Fact]
    public async Task A_provisioned_user_carries_the_employee_id_attribute_the_mapper_reads()
    {
        string? userBody = null;
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
                return Json(new { access_token = "x", expires_in = 60 });
            userBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var created = new HttpResponseMessage(HttpStatusCode.Created);
            created.Headers.Location = new Uri("http://kc/admin/realms/hrmanagement/users/" + Guid.NewGuid());
            return created;
        });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Keycloak:Authority"] = "http://kc/realms/hrmanagement",
            ["Keycloak:DirectorySync:Enabled"] = "true",
            ["Keycloak:DirectorySync:AdminClientId"] = "directory-sync",
            ["Keycloak:DirectorySync:AdminClientSecret"] = "x"
        }).Build();
        var service = new KeycloakUserProvisioningService(new HttpClient(handler), configuration, NullLogger<KeycloakUserProvisioningService>.Instance);
        var employee = Guid.NewGuid();

        await service.TryEnsureUserAsync("e.user", "e@example.test", "E User", employee, tenantId: Guid.NewGuid());

        Assert.NotNull(userBody);
        using var json = JsonDocument.Parse(userBody!);
        var attributes = json.RootElement.GetProperty("attributes");
        Assert.Equal(employee.ToString("D"), attributes.GetProperty("employee_id")[0].GetString());
        Assert.False(attributes.TryGetProperty("employeeId", out _));
    }
}
