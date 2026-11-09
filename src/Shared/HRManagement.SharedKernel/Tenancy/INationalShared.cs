namespace HRManagement.SharedKernel.Tenancy;

/// <summary>
/// PLAN-48 (D-TEN-2): a national settings entity. Its rows that carry <see cref="NationalTenant.Id"/> are readable by every
/// agency and writable only by the national tenant (or an explicit seed scope).
/// <para>
/// Read such an entity through <c>OwnOrNational</c> or <c>NationalOnly</c> (HRManagement.ServiceDefaults), never through a
/// general tenant-filter bypass. Under forced row security the <c>national_shared_read</c> policy gives the same read.
/// </para>
/// Never put this marker on an entity that holds personal data.
/// </summary>
public interface INationalShared
{
}
