using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HRManagement.SharedKernel.Notifications;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.Notifications;

/// <summary>
/// COM-030 (PLAN-30 F1): generic SMS gateway adapter — one HTTP POST of a JSON body built from
/// <c>Notifications:Candidate:Sms:BodyTemplate</c> to <c>Sms:Endpoint</c>, with the token from secret configuration in the
/// <c>Sms:AuthHeaderName</c> header. The named client has the repository's outbound timeout + circuit breaker
/// (<see cref="HttpClientResilienceExtensions.AddHrOutboundResilience"/>, no retry: the dispatcher reschedules). It is a
/// plain client — never the service-authenticated one, which would leak the service token and tenant header.
/// <para>2xx → Sent (provider id read from a top-level <c>messageId</c>/<c>id</c> when the answer is JSON); 408/429/5xx,
/// timeouts, an open circuit and transport errors → Transient; other 4xx → Permanent.</para>
/// </summary>
public sealed class HttpSmsNotificationSender(
    IHttpClientFactory httpClientFactory,
    IOptions<CandidateNotificationOptions> options) : INotificationChannelSender
{
    public const string HttpClientName = "CandidateNotifications.Sms";

    public NotificationChannel Channel => NotificationChannel.Sms;

    public async Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct)
    {
        var sms = options.Value.Sms;
        if (string.IsNullOrWhiteSpace(sms.Endpoint)
            || !Uri.TryCreate(sms.Endpoint, UriKind.Absolute, out var endpoint))
            return new ChannelSendResult.NotConfigured("SMS endpoint not configured");
        if (string.IsNullOrWhiteSpace(message.RecipientAddress))
            return new ChannelSendResult.Permanent("Missing recipient");

        string body;
        try
        {
            body = BuildBody(sms.BodyTemplate, message.RecipientAddress, sms.SenderId, message.Body, message.IdempotencyKey);
        }
        catch (JsonException)
        {
            return new ChannelSendResult.NotConfigured("SMS body template is not valid JSON");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(sms.AuthToken))
            request.Headers.TryAddWithoutValidation(
                string.IsNullOrWhiteSpace(sms.AuthHeaderName) ? "Authorization" : sms.AuthHeaderName.Trim(),
                sms.AuthToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", message.CorrelationId);

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
                return new ChannelSendResult.Sent(await TryReadProviderIdAsync(response, ct));
            return status >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                ? new ChannelSendResult.Transient($"HTTP {status}")
                : new ChannelSendResult.Permanent($"HTTP {status}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or global::Polly.Timeout.TimeoutRejectedException
                                       or global::Polly.CircuitBreaker.BrokenCircuitException)
        {
            return new ChannelSendResult.Transient(ChannelSendResult.SanitizedReason(ex));
        }
    }

    /// <summary>
    /// Fills the JSON template: every string value containing <c>{to}</c>, <c>{sender}</c>, <c>{text}</c> or
    /// <c>{reference}</c> gets the values substituted, and the serializer encodes them (no raw JSON splicing).
    /// </summary>
    public static string BuildBody(string? template, string to, string? sender, string text, string reference)
    {
        var node = JsonNode.Parse(string.IsNullOrWhiteSpace(template) ? SmsChannelOptions.DefaultBodyTemplate : template)
                   ?? throw new JsonException("Empty SMS body template.");
        var values = new Dictionary<string, string>
        {
            ["{to}"] = to,
            ["{sender}"] = sender ?? string.Empty,
            ["{text}"] = text,
            ["{reference}"] = reference
        };
        return Fill(node, values).ToJsonString();
    }

    private static JsonNode Fill(JsonNode node, IReadOnlyDictionary<string, string> values)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (obj[key] is { } child)
                        obj[key] = Fill(child.DeepClone(), values);
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } child)
                        array[i] = Fill(child.DeepClone(), values);
                }
                return array;
            case JsonValue value when value.TryGetValue<string>(out var s):
                var filled = values.Aggregate(s, (current, kv) => current.Replace(kv.Key, kv.Value, StringComparison.Ordinal));
                return JsonValue.Create(filled)!;
            default:
                return node;
        }
    }

    private static async Task<string?> TryReadProviderIdAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            foreach (var name in new[] { "messageId", "id" })
            {
                if (doc.RootElement.TryGetProperty(name, out var id))
                {
                    var text = id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText();
                    return text is { Length: > 0 } ? text[..Math.Min(text.Length, 200)] : null;
                }
            }
        }
        catch (JsonException)
        {
            // Accepted (2xx) without a readable id.
        }
        return null;
    }
}
