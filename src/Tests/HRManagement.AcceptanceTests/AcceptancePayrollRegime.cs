namespace HRManagement.AcceptanceTests;

/// <summary>
/// PLAN-54 (found by the pull request's Acceptance run): a stated test precondition of the acceptance classes that calculate a
/// Regular payroll run of agency A on a stack other classes share.
/// <para>
/// A run is refused while any employee of the agency has a current employment record without a service regime, or an open
/// regime exception (PLAN-53 §12: the gate is the product's, on purpose). The acceptance seed classifies its own employees
/// (<c>AcceptancePayrollRegimePatch</c>), but a class that runs earlier on the same stack may add an employment record after the
/// seed (AC-IAM-50b adds one through the form), and the classes of a group run in an order that changes from run to run. So a
/// payroll class applies the seed's classification again, to the records added since: the same regime, the same resolution of
/// the «unclassified» exceptions those records opened. Exceptions of any other type are left as they are and still block. The
/// gate itself is not what these classes prove.
/// </para>
/// </summary>
internal static class AcceptancePayrollRegime
{
    private const string Core = "hr_core_db";
    private const string Regime = "law50";

    /// <summary>Classifies the agency's current employment records that have no regime; returns how many, and how many exceptions it resolved.</summary>
    public static async Task<(long Records, long Exceptions)> ClassifyRecordsAddedSinceTheSeedAsync(string tenantId)
    {
        var late =
            $"SELECT r.\"Id\" FROM \"EmploymentRecords\" r WHERE r.\"TenantId\"='{tenantId}' AND r.\"EndDate\" IS NULL AND r.\"LegalRegimeId\" IS NULL";
        var records = await AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(Core, $"SELECT count(*) FROM ({late}) x;");
        if (records == 0)
            return (0, 0);

        var openOfThem =
            $"e.\"TenantId\"='{tenantId}' AND e.\"Status\"=0 AND e.\"ExceptionType\"=0 AND " +
            $"(e.\"EmploymentRecordId\" IN ({late}) OR (e.\"EmploymentRecordId\" IS NULL AND e.\"EmployeeId\" IN " +
            $"(SELECT r.\"EmployeeId\" FROM \"EmploymentRecords\" r WHERE r.\"TenantId\"='{tenantId}' AND r.\"EndDate\" IS NULL AND r.\"LegalRegimeId\" IS NULL)))";
        var exceptions = await AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(Core, $"SELECT count(*) FROM \"ServiceRegimeExceptions\" e WHERE {openOfThem};");
        // One call, one transaction: the exceptions first (they are found through the records that still have no regime).
        await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(Core,
            $"UPDATE \"ServiceRegimeExceptions\" e SET \"Status\"=1, \"ResolvedRegimeId\"='{Regime}', \"Resolution\"='تصنيف بذرة القبول: القانون 50/2004', " +
            $"\"ResolvedAt\"=now(), \"UpdatedAt\"=now() WHERE {openOfThem}; " +
            $"UPDATE \"EmploymentRecords\" SET \"LegalRegimeId\"='{Regime}', \"UpdatedAt\"=now() " +
            $"WHERE \"TenantId\"='{tenantId}' AND \"EndDate\" IS NULL AND \"LegalRegimeId\" IS NULL;");
        return (records, exceptions);
    }
}
