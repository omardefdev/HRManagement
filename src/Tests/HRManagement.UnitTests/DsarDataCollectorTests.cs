using System.Text.RegularExpressions;
using Compliance.Domain.Entities;
using Compliance.Domain.Enums;
using Compliance.Infrastructure.Privacy;
using HRManagement.SharedKernel;

namespace HRManagement.UnitTests;

/// <summary>AC-27: DSAR collector masks PII and excludes raw national IDs.</summary>
public class DsarDataCollectorTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid EmployeeId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly Guid ExporterId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    private static readonly Guid RequesterId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");

    [Fact]
    public async Task CollectAsync_excludes_raw_national_id_and_forbidden_fields()
    {
        var sources = new IDsarSubjectSource[]
        {
            new StubSource("HR.Core", new DsarDiscoveryField("HR.Core", "displayNationalId", PiiMask.NationalId("12345678901")),
                new DsarDiscoveryField("HR.Core", "NationalId", "12345678901"),
                new DsarDiscoveryField("HR.Core", "healthNotes", "secret"),
                new DsarDiscoveryField("HR.Core", "fullName", "Test User"))
        };

        var collector = new DsarDataCollectorService(sources, Array.Empty<IDsarCandidateSubjectSource>());
        var request = new DsarRequest
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            SubjectEmployeeId = EmployeeId,
            RequestType = DsarRequestType.Access,
            RequestedAt = DateTime.UtcNow.AddDays(-1)
        };

        var package = await collector.CollectAsync(request, ExporterId, RequesterId);

        var json = System.Text.Json.JsonSerializer.Serialize(package);
        Assert.DoesNotMatch(new Regex(@"\b12345678901\b"), json);
        Assert.DoesNotContain("healthNotes", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test User", json);
        Assert.Equal(ExporterId, package.ExportedByUserId);
        Assert.Equal(RequesterId, package.RequesterUserId);
        Assert.False(string.IsNullOrWhiteSpace(package.PackageContentHash));
        Assert.Equal("PRV-002", package.Card);
    }

    private sealed class StubSource(string name, params DsarDiscoveryField[] fields) : IDsarSubjectSource
    {
        public string SourceName => name;

        public Task<(bool Collected, string? Error, IReadOnlyList<DsarDiscoveryField> Fields)> FetchAsync(
            Guid tenantId, Guid employeeId, CancellationToken cancellationToken) =>
            Task.FromResult<(bool, string?, IReadOnlyList<DsarDiscoveryField>)>((true, null, fields));
    }
}
