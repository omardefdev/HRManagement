using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HRManagement.E2E;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-13: 100+ parallel code generations produce unique AssetTag codes.</summary>
public class Ac13ParallelCodeGenerationTests
{
    [SkippableFact]
    public async Task Parallel_generate_produces_unique_asset_tags()
    {
        AcceptanceEnv.RequireEnabled();
        const int batchSize = 120;
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, correlationId) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-13", correlationId);
        recorder.WriteMetadata("AC-13 parallel code generation", persona.Id);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(persona, correlationId);
        var codes = new System.Collections.Concurrent.ConcurrentBag<string>();
        var statusCodes = new System.Collections.Concurrent.ConcurrentBag<int>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, batchSize),
            new ParallelOptions { MaxDegreeOfParallelism = 30 },
            async (_, _) =>
            {
                var payload = new CodeGenerationRequest
                {
                    EntityKind = CodeEntityKind.AssetTag,
                    TenantId = AcceptanceSeedIds.AgencyAId,
                    Preview = false
                };
                var resp = await http.PostAsync(
                    "api/hr-core/api/codes/generate",
                    JsonContent(payload));
                statusCodes.Add((int)resp.StatusCode);
                if (resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(body);
                    codes.Add(doc.RootElement.GetProperty("code").GetString()!);
                }
            });

        recorder.CaptureHttp("generate-batch", "POST", "/api/hr-core/api/codes/generate",
            statusCodes.All(c => c == 200) ? 200 : 400,
            new
            {
                requested = batchSize,
                successCount = codes.Count,
                distinct = codes.Distinct().Count(),
                statusCounts = statusCodes.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count())
            });

        Assert.All(statusCodes, status => Assert.Equal(200, status));
        Assert.Equal(batchSize, codes.Count);
        Assert.Equal(batchSize, codes.Distinct(StringComparer.Ordinal).Count());

        recorder.WriteDbChecks(new
        {
            requested = batchSize,
            uniqueCodes = codes.Distinct().Count(),
            sampleCodes = codes.Take(5).ToArray()
        });
        recorder.WriteAfter(new { uniqueCount = codes.Distinct().Count() });
        recorder.WriteResult("Pass");
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
