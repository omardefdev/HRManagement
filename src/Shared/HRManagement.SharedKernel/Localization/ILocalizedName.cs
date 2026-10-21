namespace HRManagement.SharedKernel.Localization;

/// <summary>MDM / reference entities with Arabic-primary bilingual names.</summary>
public interface ILocalizedName
{
    string NameAr { get; }
    string? NameEn { get; }
    string? Code { get; }
}
