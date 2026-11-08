using System.Net;
using System.Text;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>PLAN-46: every status code, a timeout, no address, no tenant. A failure is an answer, and only the caller's cancellation throws.</summary>
[Collection("TenantFlow")]
public sealed class Wf046HttpWorkflowApprovalClientTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid Instance = Guid.Parse("30000000-0000-4000-8000-000000000001");

    private static readonly WorkflowApprovalStartRequest Request = new(
        "CardRequest", Guid.NewGuid(), "ID-CARD-ISSUANCE", "CardRequest:1:1", [], null, "بطاقة تعريف", "/ui/idcard/CardRequests/Details/1", null);

    public Wf046HttpWorkflowApprovalClientTests() => TenantIdFlow.Set(Tenant);

    public void Dispose() => TenantIdFlow.Set(null);

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, WorkflowApprovalErrorCodes.Invalid)]
    [InlineData(HttpStatusCode.Unauthorized, WorkflowApprovalErrorCodes.Forbidden)]
    [InlineData(HttpStatusCode.Forbidden, WorkflowApprovalErrorCodes.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, WorkflowApprovalErrorCodes.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity, WorkflowApprovalErrorCodes.DefinitionMissing)]
    [InlineData(HttpStatusCode.NotFound, WorkflowApprovalErrorCodes.EngineUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, WorkflowApprovalErrorCodes.EngineUnavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, WorkflowApprovalErrorCodes.EngineUnavailable)]
    public async Task A_failed_start_names_its_code(HttpStatusCode status, string code)
    {
        var result = await Client(_ => Respond(status, "{\"error\":\"السبب\"}")).StartAsync(Request, default);

        Assert.False(result.Success);
        Assert.Null(result.InstanceId);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal("السبب", result.Error);
    }

    [Fact]
    public async Task A_failure_with_no_body_still_has_a_message()
    {
        var result = await Client(_ => Respond(HttpStatusCode.BadGateway, "<html>")).StartAsync(Request, default);

        Assert.Equal(WorkflowApprovalErrorCodes.EngineUnavailable, result.ErrorCode);
        Assert.Equal(WorkflowApprovalErrorCodes.EngineUnavailableMessage, result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.OK, false)]
    public async Task A_start_carries_the_instance_and_whether_it_runs(HttpStatusCode status, bool active)
    {
        var body = $"{{\"id\":\"{Instance}\",\"status\":\"{(active ? "InProgress" : "Completed")}\",\"alreadyActive\":{active.ToString().ToLowerInvariant()}}}";

        var result = await Client(_ => Respond(status, body)).StartAsync(Request, default);

        Assert.True(result.Success);
        Assert.Equal(Instance, result.InstanceId);
        Assert.Equal(active, result.AlreadyActive);
    }

    [Fact]
    public async Task A_start_with_no_instance_in_the_answer_is_not_a_success()
    {
        var result = await Client(_ => Respond(HttpStatusCode.OK, "{\"id\":\"00000000-0000-0000-0000-000000000000\"}")).StartAsync(Request, default);

        Assert.False(result.Success);
        Assert.Equal(WorkflowApprovalErrorCodes.EngineUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task A_start_sends_the_record_tenant_to_the_contract_path()
    {
        HttpRequestMessage? sent = null;
        await Client(request =>
        {
            sent = request;
            return Respond(HttpStatusCode.Created, $"{{\"id\":\"{Instance}\",\"status\":\"InProgress\",\"alreadyActive\":true}}");
        }).StartAsync(Request, default);

        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.Equal("/api/workflow-instances", sent.RequestUri!.AbsolutePath);
        Assert.Equal(Tenant.ToString("D"), Assert.Single(sent.Headers.GetValues(TenantIdMiddleware.HeaderName)));
    }

    [Fact]
    public async Task The_flow_tenant_beats_the_tenant_of_the_request()
    {
        HttpRequestMessage? sent = null;
        var context = new DefaultHttpContext();
        context.Items[TenantIdMiddleware.ItemKey] = Guid.NewGuid();

        await Client(request =>
        {
            sent = request;
            return Respond(HttpStatusCode.Created, $"{{\"id\":\"{Instance}\",\"status\":\"InProgress\",\"alreadyActive\":true}}");
        }, context: context).StartAsync(Request, default);

        Assert.Equal(Tenant.ToString("D"), Assert.Single(sent!.Headers.GetValues(TenantIdMiddleware.HeaderName)));
    }

    [Fact]
    public async Task With_no_tenant_nothing_leaves_this_service()
    {
        TenantIdFlow.Set(null);
        var calls = 0;
        var client = Client(_ =>
        {
            calls++;
            return Respond(HttpStatusCode.OK, "{}");
        });

        var start = await client.StartAsync(Request, default);
        var cancel = await client.CancelAsync(Instance, "سبب", default);
        var status = await client.GetStatusAsync(Instance, default);

        Assert.Equal(0, calls);
        Assert.Equal(WorkflowApprovalErrorCodes.Invalid, start.ErrorCode);
        Assert.Equal(WorkflowApprovalErrorCodes.NoTenantMessage, start.Error);
        Assert.Equal(WorkflowApprovalErrorCodes.Invalid, cancel.ErrorCode);
        Assert.Null(status);
    }

    [Fact]
    public async Task With_no_address_nothing_is_assumed()
    {
        var client = Client(_ => throw new InvalidOperationException("not called"), baseAddress: null);

        var start = await client.StartAsync(Request, default);
        var cancel = await client.CancelAsync(Instance, "سبب", default);

        Assert.False(start.Success);
        Assert.Equal(WorkflowApprovalErrorCodes.EngineUnavailable, start.ErrorCode);
        Assert.False(cancel.Success);
        Assert.Equal(WorkflowApprovalErrorCodes.EngineUnavailable, cancel.ErrorCode);
        Assert.Null(await client.GetStatusAsync(Instance, default));
    }

    [Fact]
    public async Task A_timeout_and_a_network_failure_are_answers()
    {
        var timeout = Client(_ => throw new TaskCanceledException("timeout"));
        var network = Client(_ => throw new HttpRequestException("refused"));

        Assert.Equal(WorkflowApprovalErrorCodes.EngineUnavailable, (await timeout.StartAsync(Request, default)).ErrorCode);
        Assert.Equal(WorkflowApprovalErrorCodes.EngineUnavailable, (await network.StartAsync(Request, default)).ErrorCode);
        Assert.Equal(WorkflowApprovalErrorCodes.EngineUnavailable, (await timeout.CancelAsync(Instance, "سبب", default)).ErrorCode);
        Assert.Null(await network.GetStatusAsync(Instance, default));
    }

    [Fact]
    public async Task Only_the_caller_cancellation_throws()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var client = Client(_ => throw new OperationCanceledException(source.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.StartAsync(Request, source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CancelAsync(Instance, "سبب", source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetStatusAsync(Instance, source.Token));
    }

    [Fact]
    public async Task A_cancel_that_withdrew_says_so()
    {
        HttpRequestMessage? sent = null;
        var result = await Client(request =>
        {
            sent = request;
            return Respond(HttpStatusCode.OK, "{}");
        }).CancelAsync(Instance, "سبب", default);

        Assert.True(result.Success);
        Assert.False(result.AlreadyTerminal);
        Assert.Equal($"/api/workflow-instances/{Instance:D}/cancel", sent!.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("Completed", WorkflowApprovalOutcomes.Approved)]
    [InlineData("Rejected", WorkflowApprovalOutcomes.Rejected)]
    [InlineData("Cancelled", WorkflowApprovalOutcomes.Cancelled)]
    public async Task A_cancel_of_a_finished_instance_reports_its_outcome_and_not_a_success(string engineStatus, string outcome)
    {
        var result = await Client(_ => Respond(HttpStatusCode.Conflict, $"{{\"error\":\"منتهٍ\",\"status\":\"{engineStatus}\"}}"))
            .CancelAsync(Instance, "سبب", default);

        Assert.False(result.Success);
        Assert.True(result.AlreadyTerminal);
        Assert.Equal(outcome, result.FinalOutcome);
        Assert.Null(result.ErrorCode);
    }

    [Theory]
    [InlineData("{\"error\":\"قرار متزامن\"}")]
    [InlineData("{\"error\":\"جارٍ\",\"status\":\"InProgress\"}")]
    public async Task A_cancel_that_lost_to_a_decision_being_written_is_a_conflict(string body)
    {
        var result = await Client(_ => Respond(HttpStatusCode.Conflict, body)).CancelAsync(Instance, "سبب", default);

        Assert.False(result.Success);
        Assert.False(result.AlreadyTerminal);
        Assert.Equal(WorkflowApprovalErrorCodes.Conflict, result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, WorkflowApprovalErrorCodes.Invalid)]
    [InlineData(HttpStatusCode.Forbidden, WorkflowApprovalErrorCodes.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, WorkflowApprovalErrorCodes.EngineUnavailable)]
    public async Task A_failed_cancel_names_its_code(HttpStatusCode status, string code)
    {
        var result = await Client(_ => Respond(status, "")).CancelAsync(Instance, "سبب", default);

        Assert.False(result.Success);
        Assert.Equal(code, result.ErrorCode);
    }

    [Fact]
    public async Task The_status_reads_the_contract_path()
    {
        HttpRequestMessage? sent = null;
        var body = $$"""
            {"instanceId":"{{Instance}}","entityType":"CardRequest","entityId":"{{Guid.NewGuid()}}","workflowCode":"ID-CARD-ISSUANCE",
             "definitionVersion":2,"status":"Rejected","decisions":[{"level":1,"roleRequired":"idcard","userId":null,"action":"Rejected","at":"2026-11-07T10:00:00Z"}],
             "startedAt":"2026-11-07T09:00:00Z","completedAt":"2026-11-07T10:00:00Z","approvalUrl":"/ui/hr-core/WorkflowApprovals/Details/1","reason":"ناقص"}
            """;

        var status = await Client(request =>
        {
            sent = request;
            return Respond(HttpStatusCode.OK, body);
        }).GetStatusAsync(Instance, default);

        Assert.Equal($"/api/workflow-instances/{Instance:D}/external-status", sent!.RequestUri!.AbsolutePath);
        Assert.Equal(Tenant.ToString("D"), Assert.Single(sent.Headers.GetValues(TenantIdMiddleware.HeaderName)));
        Assert.Equal(WorkflowApprovalOutcomes.Rejected, status!.Status);
        Assert.Equal("ناقص", status.Reason);
        Assert.Equal("idcard", Assert.Single(status.Decisions).RoleRequired);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_status_the_engine_does_not_give_is_null(HttpStatusCode status) =>
        Assert.Null(await Client(_ => Respond(status, "")).GetStatusAsync(Instance, default));

    [Theory]
    [InlineData(null, WorkflowApprovalErrorCodes.EngineUnavailableMessage)]
    [InlineData(WorkflowApprovalErrorCodes.EngineUnavailable, WorkflowApprovalErrorCodes.EngineUnavailableMessage)]
    [InlineData(WorkflowApprovalErrorCodes.DefinitionMissing, WorkflowApprovalErrorCodes.DefinitionMissingMessage)]
    [InlineData(WorkflowApprovalErrorCodes.Forbidden, WorkflowApprovalErrorCodes.ForbiddenMessage)]
    [InlineData(WorkflowApprovalErrorCodes.Conflict, WorkflowApprovalErrorCodes.ConflictMessage)]
    [InlineData(WorkflowApprovalErrorCodes.Invalid, WorkflowApprovalErrorCodes.InvalidMessage)]
    public void Each_code_has_a_message_a_person_can_read(string? code, string message) =>
        Assert.Equal(message, WorkflowApprovalErrorCodes.MessageFor(code));

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpWorkflowApprovalClient Client(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? baseAddress = "http://hr-core/",
        HttpContext? context = null) =>
        new(new StubFactory(respond, baseAddress), new HttpContextAccessor { HttpContext = context }, NullLogger<HttpWorkflowApprovalClient>.Instance);

    private sealed class StubFactory(Func<HttpRequestMessage, HttpResponseMessage> respond, string? baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(HttpWorkflowApprovalClient.HttpClientName, name);
            return new HttpClient(new StubHandler(respond)) { BaseAddress = baseAddress is null ? null : new Uri(baseAddress) };
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
