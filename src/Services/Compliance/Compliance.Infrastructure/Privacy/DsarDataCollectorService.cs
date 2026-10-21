using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Compliance.Domain.Entities;
using HRManagement.SharedKernel;

namespace Compliance.Infrastructure.Privacy;

public sealed class DsarDataCollectorService(
    IEnumerable<IDsarSubjectSource> sources,
    IEnumerable<IDsarCandidateSubjectSource> candidateSources) : IDsarDataCollectorService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static readonly Regex RawNationalIdPattern = new(@"\b\d{11}\b", RegexOptions.Compiled);

    public async Task<DsarDiscoveryPackage> CollectAsync(
        DsarRequest request,
        Guid exportedByUserId,
        Guid? requesterUserId,
        CancellationToken cancellationToken = default)
    {
        var sections = new List<DsarDiscoverySourceSection>();
        var fields = new List<DsarDiscoveryField>();

        if (request.SubjectEmployeeId is Guid employeeId)
        {
            foreach (var source in sources)
            {
                var (collected, error, sourceFields) = await source.FetchAsync(
                    request.TenantId, employeeId, cancellationToken);

                sections.Add(new DsarDiscoverySourceSection(source.SourceName, collected, error));
                fields.AddRange(SanitizeFields(sourceFields));
            }

            if (request.SubjectCitizenApplicationId is Guid linkedAppId)
            {
                foreach (var source in candidateSources)
                {
                    var (collected, error, sourceFields) = await source.FetchAsync(
                        request.TenantId, linkedAppId, request.SubjectNationalIdHash, cancellationToken);

                    sections.Add(new DsarDiscoverySourceSection(source.SourceName + ".Linked", collected, error));
                    fields.AddRange(SanitizeFields(sourceFields));
                }
            }
        }
        else if (request.SubjectCitizenApplicationId is Guid citizenAppId
                 || !string.IsNullOrWhiteSpace(request.SubjectNationalIdHash))
        {
            foreach (var source in candidateSources)
            {
                var (collected, error, sourceFields) = await source.FetchAsync(
                    request.TenantId,
                    request.SubjectCitizenApplicationId,
                    request.SubjectNationalIdHash,
                    cancellationToken);

                sections.Add(new DsarDiscoverySourceSection(source.SourceName, collected, error));
                fields.AddRange(SanitizeFields(sourceFields));
            }
        }
        else
        {
            throw new InvalidOperationException("SubjectEmployeeId أو SubjectCitizenApplicationId أو SubjectNationalIdHash مطلوب للتجميع.");
        }

        fields.Add(new DsarDiscoveryField("Compliance", "requestType", request.RequestType.ToString()));
        fields.Add(new DsarDiscoveryField("Compliance", "requestedAt", request.RequestedAt));
        if (!string.IsNullOrWhiteSpace(request.SubjectNationalIdHash))
        {
            fields.Add(new DsarDiscoveryField(
                "Compliance",
                "subjectNationalIdHash",
                request.SubjectNationalIdHash));
        }

        if (request.SubjectCitizenApplicationId is Guid appId)
        {
            fields.Add(new DsarDiscoveryField("Compliance", "subjectCitizenApplicationId", appId));
        }

        var exportedAt = DateTime.UtcNow;
        var hash = ComputePackageHash(request, exportedByUserId, requesterUserId, exportedAt, sections, fields);

        return new DsarDiscoveryPackage(
            Version: 1,
            Card: "PRV-002",
            TenantId: request.TenantId,
            SubjectEmployeeId: request.SubjectEmployeeId,
            SubjectNationalIdHash: request.SubjectNationalIdHash,
            SubjectCitizenApplicationId: request.SubjectCitizenApplicationId,
            ExportedAtUtc: exportedAt,
            ExportedByUserId: exportedByUserId,
            RequesterUserId: requesterUserId,
            PackageContentHash: hash,
            Sources: sections,
            Fields: fields);
    }

    internal static IReadOnlyList<DsarDiscoveryField> SanitizeFields(IReadOnlyList<DsarDiscoveryField> fields)
    {
        var result = new List<DsarDiscoveryField>(fields.Count);
        foreach (var field in fields)
        {
            if (IsForbiddenField(field.Name))
                continue;

            var value = field.Value;
            if (value is string s)
            {
                if (RawNationalIdPattern.IsMatch(s))
                    continue;
                if (s.Length == 64 && field.Name.Contains("national", StringComparison.OrdinalIgnoreCase)
                    && !field.Name.Contains("hash", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            result.Add(field with { Value = MaskIfNationalId(field.Name, value) });
        }

        return result;
    }

    private static bool IsForbiddenField(string name) =>
        name.Contains("health", StringComparison.OrdinalIgnoreCase)
        || name.Contains("medical", StringComparison.OrdinalIgnoreCase)
        || name.Equals("NationalId", StringComparison.OrdinalIgnoreCase)
        || name.Equals("nationalId", StringComparison.OrdinalIgnoreCase);

    private static object? MaskIfNationalId(string name, object? value)
    {
        if (value is not string text)
            return value;

        if (name.Contains("national", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("hash", StringComparison.OrdinalIgnoreCase))
            return PiiMask.NationalId(text);

        return text;
    }

    private static string ComputePackageHash(
        DsarRequest request,
        Guid exportedBy,
        Guid? requester,
        DateTime exportedAt,
        IReadOnlyList<DsarDiscoverySourceSection> sources,
        IReadOnlyList<DsarDiscoveryField> fields)
    {
        var payload = JsonSerializer.Serialize(new
        {
            request.Id,
            request.TenantId,
            request.SubjectEmployeeId,
            request.SubjectCitizenApplicationId,
            request.SubjectNationalIdHash,
            exportedBy,
            requester,
            exportedAt,
            sources,
            fields
        }, JsonOptions);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
