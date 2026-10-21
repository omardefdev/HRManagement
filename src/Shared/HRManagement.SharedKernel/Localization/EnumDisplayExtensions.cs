using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace HRManagement.SharedKernel.Localization;

/// <summary>L10N-003: localized enum labels via [Display(Name)] on enum members.</summary>
public static class EnumDisplayExtensions
{
    private static readonly ConcurrentDictionary<(Type Type, string Name), string?> DisplayCache = new();
    private static readonly ConcurrentDictionary<string, Type?> EnumTypeByShortName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<Type?[]> KnownEnumTypes = new(ScanEnumTypes);

    public static string GetDisplayName<TEnum>(TEnum value) where TEnum : struct, Enum
        => GetDisplayName(typeof(TEnum), value);

    public static string GetDisplayName(Enum value)
        => GetDisplayName(value.GetType(), value);

    public static string GetDisplayName(Type enumType, object value)
    {
        if (value is null)
            return "—";

        var underlying = enumType.IsEnum ? enumType : Nullable.GetUnderlyingType(enumType) ?? enumType;
        if (!underlying.IsEnum)
            return value.ToString() ?? "—";

        var name = Enum.GetName(underlying, value);
        if (name is null)
            return value.ToString() ?? "—";

        return DisplayCache.GetOrAdd((underlying, name), static key =>
        {
            var field = key.Type.GetField(key.Name, BindingFlags.Public | BindingFlags.Static);
            if (field is null)
                return key.Name;

            var display = field.GetCustomAttribute<DisplayAttribute>();
            return display?.GetName() ?? display?.Name ?? key.Name;
        }) ?? name;
    }

    public static string GetStoredEnumDisplayName(string enumTypeName, string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
            return "—";

        var enumType = ResolveEnumType(enumTypeName);
        if (enumType is not null && Enum.TryParse(enumType, storedValue, ignoreCase: true, out var parsed))
            return GetDisplayName(enumType, parsed);

        return CommonStatusLabels.TryGetValue(storedValue.Trim(), out var label)
            ? label
            : storedValue;
    }

    private static readonly Dictionary<string, string> CommonStatusLabels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["active"] = "نشط",
            ["inactive"] = "غير نشط",
            ["pending"] = "قيد الانتظار",
            ["approved"] = "معتمد",
            ["rejected"] = "مرفوض",
            ["draft"] = "مسودة",
            ["closed"] = "مغلق",
            ["open"] = "مفتوح",
            ["resolved"] = "محلول",
            ["dismissed"] = "مُستبعد",
            ["underreview"] = "قيد المراجعة",
            ["released"] = "مُفرج عنه",
            ["critical"] = "حرج",
            ["high"] = "مرتفع",
            ["medium"] = "متوسط",
            ["fraud"] = "احتيال",
            ["proofoflife"] = "إثبات حياة",
            ["manual"] = "يدوي",
        };

    public static Type? ResolveEnumType(string enumTypeName)
    {
        if (string.IsNullOrWhiteSpace(enumTypeName))
            return null;

        return EnumTypeByShortName.GetOrAdd(enumTypeName.Trim(), static name =>
        {
            foreach (var type in KnownEnumTypes.Value)
            {
                if (type is null)
                    continue;

                if (string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type.FullName, name, StringComparison.OrdinalIgnoreCase))
                    return type;
            }

            return null;
        });
    }

    private static Type?[] ScanEnumTypes()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.FullName?.StartsWith("System.", StringComparison.Ordinal) != true
                        && a.FullName?.StartsWith("Microsoft.", StringComparison.Ordinal) != true);

        return assemblies
            .SelectMany(SafeGetTypes)
            .Where(t => t.IsEnum)
            .Distinct()
            .Cast<Type?>()
            .ToArray();
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
