namespace HRManagement.SharedKernel.Tenancy;

/// <summary>
/// Platform default tenant used for TEN-002 backfill until TEN-003 assigns real agencies.
/// </summary>
public static class TenantDefaults
{
    public static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-4000-8000-000000000001");

    public const string DefaultCode = "PLATFORM";
    public const string DefaultNameAr = "الجهة الافتراضية";
}
