using System.Text;
using System.Text.Json;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HRManagement.ServiceDefaults.Notifications;

/// <summary>
/// COM-030 (PLAN-30 F1): lab/acceptance channel — writes each message as one UTF-8 JSON file (no BOM) named
/// <c>{sha256(idempotencyKey)}.json</c> in <c>Notifications:Candidate:FileDrop:Path</c>. A resend of the same key rewrites
/// the same file (written to a temp file, then moved over), so delivery stays idempotent. Refused in Production (here and
/// by <see cref="CandidateNotificationStartupValidator"/>), because the file holds the real address and body.
/// <para>File format (camelCase JSON): <c>template</c>, <c>channel</c> (Email|Sms), <c>recipient</c>, <c>subject</c>
/// (null for SMS), <c>body</c>, <c>idempotencyKey</c>, <c>correlationId</c>, <c>createdAt</c> (UTC, ISO 8601).</para>
/// </summary>
public sealed class FileDropNotificationSender(
    NotificationChannel channel,
    IOptions<CandidateNotificationOptions> options,
    IHostEnvironment environment) : INotificationChannelSender
{
    public const string FileExtension = ".json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Keep Arabic text readable in the file (the file is UTF-8; it is never embedded in HTML).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public NotificationChannel Channel { get; } = channel;

    /// <summary>File name of a message: lowercase hex SHA-256 of the idempotency key + <see cref="FileExtension"/>.</summary>
    public static string FileNameFor(string idempotencyKey) => PiiMask.Sha256Hex(idempotencyKey) + FileExtension;

    /// <summary>The drop folder: absolute, or relative to the content root.</summary>
    public static string? ResolveFolder(string? configuredPath, string contentRootPath) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? null
            : Path.GetFullPath(Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(contentRootPath, configuredPath));

    public async Task<ChannelSendResult> SendAsync(OutboundNotification message, CancellationToken ct)
    {
        if (environment.IsProduction())
            return new ChannelSendResult.NotConfigured("FileDrop is not allowed in Production");
        if (string.IsNullOrWhiteSpace(message.IdempotencyKey))
            return new ChannelSendResult.Permanent("Missing idempotency key");

        var folder = ResolveFolder(options.Value.FileDrop.Path, environment.ContentRootPath);
        if (folder is null)
            return new ChannelSendResult.NotConfigured("FileDrop path not configured");

        var fileName = FileNameFor(message.IdempotencyKey);
        var finalPath = Path.Combine(folder, fileName);
        var tempPath = Path.Combine(folder, $".{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(folder);
            var payload = new FileDropMessage(
                message.TemplateCode,
                Channel.ToString(),
                message.RecipientAddress,
                Channel == NotificationChannel.Sms ? null : message.Subject,
                message.Body,
                message.IdempotencyKey,
                message.CorrelationId,
                DateTime.UtcNow);
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            await File.WriteAllTextAsync(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
            File.Move(tempPath, finalPath, overwrite: true);
            return new ChannelSendResult.Sent(fileName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryDelete(tempPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            return new ChannelSendResult.Transient(ChannelSendResult.SanitizedReason(ex));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a stray temp file is harmless (dot-prefixed, .tmp).
        }
    }

    /// <summary>Shape of a FileDrop file (documented in docs/COM_001_NOTIFICATIONS.md for acceptance tests).</summary>
    public sealed record FileDropMessage(
        string? Template,
        string Channel,
        string Recipient,
        string? Subject,
        string Body,
        string IdempotencyKey,
        string? CorrelationId,
        DateTime CreatedAt);
}
