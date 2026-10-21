using System.Text.Json;
using System.Text.Json.Serialization;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Recruitment;

/// <summary>
/// HCM-026: (de)serializes <c>RecruitmentAssessmentCriteria.CriteriaJson</c>.
/// Also reads the legacy seed format <c>[{"name":"المقابلة","weight":40}]</c>.
/// </summary>
public static class AssessmentCriteriaSerializer
{
    public const decimal LegacyMaxScore = 100m;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(IReadOnlyList<AssessmentCriteriaItem> items) =>
        JsonSerializer.Serialize(items.Select(i => new ItemDto
        {
            Key = i.Key,
            NameAr = i.NameAr,
            Weight = i.Weight,
            MaxScore = i.MaxScore,
            Stage = i.Stage,
            IsMandatory = i.IsMandatory,
            Description = i.Description
        }), Options);

    /// <summary>Returns the parsed items, or an empty list when the JSON is missing or malformed.</summary>
    public static IReadOnlyList<AssessmentCriteriaItem> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var items = new List<AssessmentCriteriaItem>();
            var index = 0;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    return [];

                items.Add(IsLegacy(element)
                    ? ReadLegacy(element, index)
                    : ReadCurrent(element));
                index++;
            }

            return items;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return [];
        }
    }

    private static bool IsLegacy(JsonElement element) =>
        !TryGetProperty(element, "key", out _) && !TryGetProperty(element, "nameAr", out _);

    private static AssessmentCriteriaItem ReadLegacy(JsonElement element, int index)
    {
        var name = TryGetProperty(element, "name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? string.Empty
            : string.Empty;
        var weight = TryGetProperty(element, "weight", out var w) ? w.GetDecimal() : 0m;
        return new AssessmentCriteriaItem(
            $"item-{index + 1}",
            name,
            weight,
            LegacyMaxScore,
            AssessmentStage.Other);
    }

    private static AssessmentCriteriaItem ReadCurrent(JsonElement element)
    {
        var dto = element.Deserialize<ItemDto>(Options) ?? new ItemDto();
        return new AssessmentCriteriaItem(
            dto.Key ?? string.Empty,
            dto.NameAr ?? string.Empty,
            dto.Weight,
            dto.MaxScore,
            dto.Stage,
            dto.IsMandatory,
            dto.Description);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed class ItemDto
    {
        public string? Key { get; set; }
        public string? NameAr { get; set; }
        public decimal Weight { get; set; }
        public decimal MaxScore { get; set; }
        public AssessmentStage Stage { get; set; } = AssessmentStage.Other;
        public bool IsMandatory { get; set; } = true;
        public string? Description { get; set; }
    }
}
