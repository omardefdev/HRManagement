using System.Text.Json;

namespace HR.Core.Infrastructure.Onboarding;

public static class AgencyManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AgencyManifest LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Agency manifest not found", path);

        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    public static AgencyManifest LoadFromJson(string json)
    {
        var manifest = JsonSerializer.Deserialize<AgencyManifest>(json, JsonOptions)
                       ?? throw new InvalidOperationException("Invalid agency manifest JSON");

        Validate(manifest);
        return manifest;
    }

    public static void Validate(AgencyManifest manifest)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(manifest.PackId))
            errors.Add("packId is required");
        if (string.IsNullOrWhiteSpace(manifest.AgencyId))
            errors.Add("agencyId is required");
        if (string.IsNullOrWhiteSpace(manifest.ReleaseDigest) || !manifest.ReleaseDigest.StartsWith("sha256:", StringComparison.Ordinal))
            errors.Add("releaseDigest must start with sha256:");
        if (manifest.ReviewerRoles.Count < 2)
            errors.Add("reviewerRoles requires at least two entries (four-eyes)");
        if (!manifest.Policies.PreserveNationalMandatory)
            errors.Add("policies.preserveNationalMandatory must be true");
        if (manifest.Policies.PolicyPackIds.Count == 0)
            errors.Add("policies.policyPackIds requires at least one pack");
        if (manifest.ReferenceSubset.CatalogKinds.Count == 0)
            errors.Add("referenceSubset.catalogKinds requires at least one kind");
        if (string.IsNullOrWhiteSpace(manifest.Tenant.DisplayName))
            errors.Add("tenant.displayName is required");

        ValidateOrgStructure(manifest.OrgStructure, errors);

        if (errors.Count > 0)
            throw new InvalidOperationException($"Manifest validation failed: {string.Join("; ", errors)}");
    }

    private static void ValidateOrgStructure(AgencyOrgStructureSection org, List<string> errors)
    {
        if (org.Units.Count == 0)
            return;

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in org.Units)
        {
            if (string.IsNullOrWhiteSpace(unit.Code))
            {
                errors.Add("orgStructure.units[].code is required");
                continue;
            }

            if (!codes.Add(unit.Code.Trim()))
                errors.Add($"orgStructure duplicate code: {unit.Code}");
        }

        var roots = org.Units.Count(u => string.IsNullOrWhiteSpace(u.ParentCode));
        if (roots == 0)
            errors.Add("orgStructure requires at least one root unit (no parentCode)");

        foreach (var unit in org.Units)
        {
            if (string.IsNullOrWhiteSpace(unit.ParentCode))
                continue;

            if (!codes.Contains(unit.ParentCode.Trim()))
                errors.Add($"orgStructure unknown parentCode {unit.ParentCode} for {unit.Code}");

            if (string.Equals(unit.ParentCode, unit.Code, StringComparison.OrdinalIgnoreCase))
                errors.Add($"orgStructure cycle: {unit.Code} cannot be its own parent");
        }

        // DFS cycle detection via parent pointers
        var parentByCode = org.Units
            .Where(u => !string.IsNullOrWhiteSpace(u.ParentCode))
            .ToDictionary(u => u.Code.Trim(), u => u.ParentCode!.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var code in codes)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = code;
            while (parentByCode.TryGetValue(current, out var parent))
            {
                if (!visited.Add(current))
                {
                    errors.Add($"orgStructure cycle detected at {code}");
                    break;
                }

                current = parent;
            }
        }
    }
}
