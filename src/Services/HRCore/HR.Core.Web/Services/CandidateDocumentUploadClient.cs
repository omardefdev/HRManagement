using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.FileStorage;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Integration;

namespace HR.Core.Web.Services;

/// <summary>HCM-030 (E2): a candidate file that passed the local checks, buffered and ready to relay.</summary>
public sealed record PreparedCandidateFile(string DocumentType, string FileName, string ContentType, byte[] Content);

/// <summary>HCM-030 (E2): outcome of relaying one candidate file to File.Storage.</summary>
public abstract record CandidateDocumentUploadResult
{
    private CandidateDocumentUploadResult() { }

    /// <summary>Stored in File.Storage quarantine (scan pending).</summary>
    public sealed record Uploaded(Guid FileId, string ContentType, long Size) : CandidateDocumentUploadResult;

    /// <summary>Not stored (not configured, transport error, timeout, refusal). The reason is for logs only.</summary>
    public sealed record Failed(string Reason) : CandidateDocumentUploadResult;
}

/// <summary>HCM-030 (E6): File.Storage's view of a candidate file (values after the scan).</summary>
public sealed record CandidateFileMetadata(
    Guid FileId,
    string? OriginalName,
    string? MimeType,
    long Size,
    string? ScanStatus,
    string? Status,
    DateTime UploadedAt);

/// <summary>HCM-030 (E6): outcome of reading a candidate file's bytes.</summary>
public abstract record CandidateFileContentResult
{
    private CandidateFileContentResult() { }

    public sealed record Found(byte[] Content, string ContentType, string? FileName) : CandidateFileContentResult;

    /// <summary>Missing, another tenant's, or not Clean (File.Storage does not tell which).</summary>
    public sealed record NotFound : CandidateFileContentResult;

    public sealed record Unavailable(string Reason) : CandidateFileContentResult;
}

/// <summary>
/// HCM-030 (E2/E6): HR.Core's service-identity relay to File.Storage's service-upload endpoints
/// (<c>api/files/service-upload</c>, scope <c>file-storage.service-upload</c>, client <c>svc-hr-core</c> only). The
/// portal has no ambient tenant, so every call names the tenant explicitly (<c>X-Tenant-ID</c>).
/// </summary>
public interface ICandidateDocumentUploadClient
{
    Task<CandidateDocumentUploadResult> UploadAsync(Guid tenantId, PreparedCandidateFile file, CancellationToken ct = default);

    /// <summary>Metadata of files this service uploaded in the tenant; null when File.Storage cannot be asked.</summary>
    Task<IReadOnlyDictionary<Guid, CandidateFileMetadata>?> GetMetadataAsync(
        Guid tenantId, IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default);

    /// <summary>The bytes of a Clean file this service uploaded in the tenant.</summary>
    Task<CandidateFileContentResult> GetContentAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
}

/// <summary>
/// HCM-030 (E2): calls File.Storage DIRECTLY (<c>Integration:FileStorageBaseUrl</c>), not through the gateway: the
/// gateway's <c>/api/files</c> rules need a tenant claim that service tokens lack. Each request carries the caller proof
/// (<see cref="GatewayCallerProof"/>; a no-op without a configured secret, as in Development).
/// </summary>
public sealed class CandidateDocumentUploadClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<CandidateDocumentUploadClient> logger) : ICandidateDocumentUploadClient
{
    public const string HttpClientName = "CandidateDocumentUpload";
    public const string ConstraintProfile = "CandidateDocument";
    public const string UploadPath = "api/files/service-upload";
    public const string MetadataPath = "api/files/service-upload/metadata";
    /// <summary>File.Storage reads at most 50 ids per metadata call.</summary>
    public const int MetadataBatchSize = 50;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<CandidateDocumentUploadResult> UploadAsync(Guid tenantId, PreparedCandidateFile file, CancellationToken ct = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var part = new ByteArrayContent(file.Content);
            part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            form.Add(part, "file", file.FileName);
            form.Add(new StringContent(ConstraintProfile), "constraintProfile");

            using var request = CreateRequest(HttpMethod.Post, UploadPath, tenantId);
            request.Content = form;
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // The body is not echoed: it may be English (framework limits) and the candidate gets one fixed message.
                logger.LogWarning("Candidate document upload returned HTTP {Status}.", (int)response.StatusCode);
                return new CandidateDocumentUploadResult.Failed($"HTTP {(int)response.StatusCode}");
            }

            var uploaded = await response.Content.ReadFromJsonAsync<UploadResponseDto>(JsonOptions, ct);
            if (uploaded is null || uploaded.FileId == Guid.Empty)
                return new CandidateDocumentUploadResult.Failed("Empty File.Storage response");

            return new CandidateDocumentUploadResult.Uploaded(
                uploaded.FileId,
                string.IsNullOrWhiteSpace(uploaded.MimeType) ? file.ContentType : uploaded.MimeType,
                uploaded.Size > 0 ? uploaded.Size : file.Content.LongLength);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Transport errors, timeouts, open circuit and token failures.
            logger.LogWarning(ex, "Candidate document upload failed.");
            return new CandidateDocumentUploadResult.Failed(ex is OperationCanceledException ? "Timeout" : ex.GetType().Name);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, CandidateFileMetadata>?> GetMetadataAsync(
        Guid tenantId, IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, CandidateFileMetadata>();
        if (fileIds.Count == 0)
            return result;

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            foreach (var chunk in fileIds.Distinct().Chunk(MetadataBatchSize))
            {
                using var request = CreateRequest(HttpMethod.Post, MetadataPath, tenantId);
                request.Content = JsonContent.Create(new MetadataRequestDto(chunk));
                using var response = await client.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Candidate document metadata returned HTTP {Status}.", (int)response.StatusCode);
                    return null;
                }

                var rows = await response.Content.ReadFromJsonAsync<List<CandidateFileMetadata>>(JsonOptions, ct) ?? [];
                foreach (var row in rows)
                    result[row.FileId] = row;
            }
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Candidate document metadata failed.");
            return null;
        }
    }

    public async Task<CandidateFileContentResult> GetContentAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, $"{UploadPath}/{fileId:D}/content", tenantId);
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new CandidateFileContentResult.NotFound();
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Candidate document content {FileId} returned HTTP {Status}.", fileId, (int)response.StatusCode);
                return new CandidateFileContentResult.Unavailable($"HTTP {(int)response.StatusCode}");
            }

            // At most 5 MB (CandidateDocument profile), so it is buffered rather than streamed through.
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            return new CandidateFileContentResult.Found(bytes, contentType, fileName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Candidate document content {FileId} failed.", fileId);
            return new CandidateFileContentResult.Unavailable(ex is OperationCanceledException ? "Timeout" : ex.GetType().Name);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, Guid tenantId)
    {
        var request = new HttpRequestMessage(method, relativePath);
        // ForwardTenantHandler adds nothing on the anonymous portal (and skips when the header is already set).
        request.Headers.TryAddWithoutValidation(TenantIdMiddleware.HeaderName, tenantId.ToString("D"));
        GatewayCallerProof.Sign(request, configuration, "/" + relativePath.TrimStart('/'));
        return request;
    }

    private sealed record UploadResponseDto(Guid FileId, string? OriginalName, long Size, string? MimeType);

    private sealed record MetadataRequestDto(IReadOnlyList<Guid> FileIds);
}

/// <summary>HCM-030 (E2): registered when <c>Integration:FileStorageBaseUrl</c> is empty — every call fails closed.</summary>
public sealed class UnavailableCandidateDocumentUploadClient : ICandidateDocumentUploadClient
{
    public const string Reason = "File.Storage service upload not configured";

    public Task<CandidateDocumentUploadResult> UploadAsync(Guid tenantId, PreparedCandidateFile file, CancellationToken ct = default) =>
        Task.FromResult<CandidateDocumentUploadResult>(new CandidateDocumentUploadResult.Failed(Reason));

    public Task<IReadOnlyDictionary<Guid, CandidateFileMetadata>?> GetMetadataAsync(
        Guid tenantId, IReadOnlyCollection<Guid> fileIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CandidateFileMetadata>?>(null);

    public Task<CandidateFileContentResult> GetContentAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        Task.FromResult<CandidateFileContentResult>(new CandidateFileContentResult.Unavailable(Reason));
}

/// <summary>
/// HCM-030 (E2): local checks of the files posted with an application, before anything is relayed: count, per-file
/// size, total size, extension, declared content type and magic bytes (<see cref="FileContentProbe"/>). File.Storage
/// repeats the per-file checks.
/// </summary>
public static class CandidateDocumentFileValidator
{
    /// <summary>File.Storage refuses names longer than 256 characters.</summary>
    public const int MaxFileNameLength = 256;

    public sealed record Input(string DocumentType, IFormFile File, string FieldName);

    public sealed record Outcome(IReadOnlyList<PreparedCandidateFile> Files, IReadOnlyList<(string FieldName, string Error)> Errors)
    {
        public bool IsValid => Errors.Count == 0;
    }

    public static string TooManyFilesMessage(int maxFiles) => $"يمكن إرفاق {maxFiles} ملفات على الأكثر.";

    public static string TotalTooLargeMessage(long maxTotalBytes) =>
        $"الحجم الإجمالي للمستندات يجب ألا يتجاوز {maxTotalBytes / (1024 * 1024)} ميغابايت.";

    /// <summary>The per-file constraint: File.Storage's <c>CandidateDocument</c> profile, never above it.</summary>
    public static FileUploadConstraints Constraints(RecruitmentCandidateDocumentsOptions options) =>
        FileUploadConstraints.CandidateDocument with
        {
            MaxBytes = Math.Min(options.MaxBytes, FileUploadConstraints.CandidateDocument.MaxBytes)
        };

    public static async Task<Outcome> PrepareAsync(
        IReadOnlyList<Input> inputs,
        RecruitmentCandidateDocumentsOptions options,
        CancellationToken ct = default)
    {
        var errors = new List<(string, string)>();
        if (inputs.Count > options.MaxFiles)
            errors.Add((string.Empty, TooManyFilesMessage(options.MaxFiles)));
        if (inputs.Sum(i => i.File.Length) > options.MaxTotalBytes)
            errors.Add((string.Empty, TotalTooLargeMessage(options.MaxTotalBytes)));
        if (errors.Count > 0)
            return new Outcome([], errors);

        var constraints = Constraints(options);
        var files = new List<PreparedCandidateFile>();
        foreach (var input in inputs)
        {
            var (valid, error) = FileUploadHelper.Validate(input.File, constraints);
            if (!valid)
            {
                errors.Add((input.FieldName, error!));
                continue;
            }

            using var buffer = new MemoryStream();
            await input.File.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            var probe = await FileContentProbe.ProbeAsync(buffer, input.File.FileName, constraints, ct);
            if (!probe.Success || probe.DetectedContentType is null)
            {
                errors.Add((input.FieldName, probe.Error ?? "تعذر قراءة محتوى الملف."));
                continue;
            }

            files.Add(new PreparedCandidateFile(
                input.DocumentType, SafeFileName(input.File.FileName), probe.DetectedContentType, buffer.ToArray()));
        }

        return new Outcome(errors.Count == 0 ? files : [], errors);
    }

    /// <summary>Sanitized name, shortened (extension kept) to <see cref="MaxFileNameLength"/>.</summary>
    public static string SafeFileName(string fileName)
    {
        var name = FileUploadHelper.SanitizeFileName(fileName);
        if (name.Length <= MaxFileNameLength)
            return name;
        var extension = Path.GetExtension(name);
        return name[..(MaxFileNameLength - extension.Length)] + extension;
    }
}

public static class CandidateDocumentUploadClientExtensions
{
    public static IServiceCollection AddCandidateDocumentUploadClient(this IServiceCollection services, IConfiguration configuration)
    {
        var fileStorageBase = ServiceEndpoint.Optional(configuration, "Integration:FileStorageBaseUrl");
        if (string.IsNullOrWhiteSpace(fileStorageBase))
        {
            services.AddScoped<ICandidateDocumentUploadClient, UnavailableCandidateDocumentUploadClient>();
            return services;
        }

        services.AddServiceAuthenticatedClient(CandidateDocumentUploadClient.HttpClientName, new Uri(fileStorageBase + "/"))
            .ConfigureHttpClient(client => client.Timeout = CandidateDocumentUploadClient.Timeout);
        services.AddScoped<ICandidateDocumentUploadClient, CandidateDocumentUploadClient>();
        return services;
    }
}
