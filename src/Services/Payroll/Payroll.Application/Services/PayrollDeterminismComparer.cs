using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Payroll.Domain.Entities;

namespace Payroll.Application.Services;

/// <summary>AC-21: canonical export and bit-for-bit comparison of payroll run outputs.</summary>
public static class PayrollDeterminismComparer
{
    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public sealed record CanonicalComponent(int ComponentType, string ComponentName, decimal Amount);

    public sealed record CanonicalLine(
        Guid EmployeeId,
        decimal GrossPay,
        decimal TotalDeductions,
        decimal NetPay,
        IReadOnlyList<CanonicalComponent> Components);

    public static string ComputeAggregateInputFingerprint(IEnumerable<(Guid EmployeeId, string? Fingerprint)> snapshots)
    {
        var payload = string.Join("|",
            snapshots
                .OrderBy(s => s.EmployeeId)
                .Select(s => s.Fingerprint ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public static IReadOnlyList<CanonicalLine> ExportCanonical(
        IEnumerable<SalaryLine> lines,
        IEnumerable<SalaryLineComponent> components)
    {
        var lineList = lines.OrderBy(l => l.EmployeeId).ToList();
        if (lineList.Count == 0)
            return Array.Empty<CanonicalLine>();

        var byLine = components
            .GroupBy(c => c.SalaryLineId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return lineList.Select(line =>
        {
            var comps = byLine.GetValueOrDefault(line.Id) ?? [];
            var canonicalComps = comps
                .OrderBy(c => c.ComponentType)
                .ThenBy(c => c.ComponentName, StringComparer.Ordinal)
                .ThenBy(c => c.Amount)
                .Select(c => new CanonicalComponent((int)c.ComponentType, c.ComponentName, c.Amount))
                .ToList();

            return new CanonicalLine(
                line.EmployeeId,
                line.GrossPay,
                line.TotalDeductions,
                line.NetPay,
                canonicalComps);
        }).ToList();
    }

    public static string HashCanonical(IReadOnlyList<CanonicalLine> canonical)
    {
        var json = JsonSerializer.Serialize(canonical, CanonicalJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public static bool AreBitForBitEqual(IReadOnlyList<CanonicalLine> runA, IReadOnlyList<CanonicalLine> runB) =>
        HashCanonical(runA) == HashCanonical(runB);

    public static object? BuildDiff(IReadOnlyList<CanonicalLine> runA, IReadOnlyList<CanonicalLine> runB)
    {
        if (AreBitForBitEqual(runA, runB))
            return null;

        var mapA = runA.ToDictionary(l => l.EmployeeId);
        var mapB = runB.ToDictionary(l => l.EmployeeId);
        var allIds = mapA.Keys.Union(mapB.Keys).OrderBy(x => x).ToList();
        var diffs = new List<object>();

        foreach (var id in allIds)
        {
            mapA.TryGetValue(id, out var a);
            mapB.TryGetValue(id, out var b);
            if (a is null || b is null || !LineEquals(a, b))
            {
                diffs.Add(new
                {
                    employeeId = id,
                    runA = a,
                    runB = b
                });
            }
        }

        return new
        {
            runALineCount = runA.Count,
            runBLineCount = runB.Count,
            hashA = HashCanonical(runA),
            hashB = HashCanonical(runB),
            differences = diffs
        };
    }

    private static bool LineEquals(CanonicalLine a, CanonicalLine b) =>
        a.GrossPay == b.GrossPay
        && a.TotalDeductions == b.TotalDeductions
        && a.NetPay == b.NetPay
        && a.Components.Count == b.Components.Count
        && a.Components.Zip(b.Components).All(pair =>
            pair.First.ComponentType == pair.Second.ComponentType
            && pair.First.ComponentName == pair.Second.ComponentName
            && pair.First.Amount == pair.Second.Amount);
}
