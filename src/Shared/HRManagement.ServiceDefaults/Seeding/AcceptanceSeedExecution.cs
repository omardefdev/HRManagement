using System.Security.Cryptography;
using System.Text;
using HRManagement.SharedKernel.Tenancy;

namespace HRManagement.ServiceDefaults.Seeding;

/// <summary>
/// Async-local seed frame used only while an Acceptance v2 agency is being populated.
/// Existing demo seeders can therefore reuse their domain-rich builders without sharing IDs
/// or tenant values with the Demo profile.
/// </summary>
public static class AcceptanceSeedExecution
{
    private static readonly AsyncLocal<AcceptanceAgency?> CurrentValue = new();

    public static AcceptanceAgency? CurrentAgency => CurrentValue.Value;
    public static bool IsAcceptance => CurrentAgency is not null;
    public static Guid TenantId => CurrentAgency?.Id ?? TenantDefaults.DefaultTenantId;
    public static DateTime UtcNow => CurrentAgency is null ? DateTime.UtcNow : AcceptanceSeedCatalog.ReferenceDate;

    public static int EmployeeCount => CurrentAgency?.EmployeeCount ?? 500;
    public static int OrgUnitCount => CurrentAgency?.OrgUnitCount ?? 30;
    public static int PayableEmployeeCount => CurrentAgency?.PayableEmployeeCount ?? 0;
    public static int SeparationCount => CurrentAgency?.SeparationCount ?? 25;
    public static int ProbationCount => CurrentAgency?.Employees.Count(e => e.State == AcceptanceEmployeeState.Probation) ?? 50;
    public static int LeaveRequestCount => CurrentAgency?.Volume.LeaveRequests ?? 200;
    public static int AdvanceCount => CurrentAgency?.Volume.Advances ?? 30;
    public static int TransferCount => CurrentAgency?.Volume.Transfers ?? 40;
    public static int SecondmentCount => CurrentAgency?.Volume.Secondments ?? 20;
    public static int LoanCount => CurrentAgency?.Volume.Loans ?? 15;
    public static int TrainingEnrollmentCount => CurrentAgency?.Volume.TrainingEnrollments ?? 200;
    public static int CardCount => CurrentAgency?.Volume.IssuedCardsAndRequests ?? 450;
    public static int AssetCount => CurrentAgency?.Volume.Assets ?? 200;
    public static int AssetAssignmentCount => CurrentAgency?.Volume.AssetAssignments ?? 300;
    public static int ComplianceCaseCount => CurrentAgency?.Volume.ComplianceCases ?? 25;
    public static int DsarCount => CurrentAgency?.Volume.DsarRequests ?? 8;
    public static int FileCount => CurrentAgency?.Volume.Files ?? 0;

    public static IDisposable Push(AcceptanceAgency agency)
    {
        ArgumentNullException.ThrowIfNull(agency);
        var previous = CurrentValue.Value;
        CurrentValue.Value = agency;
        TenantIdFlow.Set(agency.Id);
        return new PopScope(previous);
    }

    public static Random CreateRandom(string purpose)
    {
        var key = $"{CurrentAgency?.Code ?? "DEMO"}|{purpose}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Random(BitConverter.ToInt32(hash, 0));
    }

    private sealed class PopScope(AcceptanceAgency? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            CurrentValue.Value = previous;
            TenantIdFlow.Set(previous?.Id);
            _disposed = true;
        }
    }
}
