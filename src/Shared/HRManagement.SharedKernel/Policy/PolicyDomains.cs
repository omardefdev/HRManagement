namespace HRManagement.SharedKernel.Policy;

/// <summary>Registered policy domains across HR microservices.</summary>
public static class PolicyDomains
{
    public const string AssetsCustody = "Assets.Custody";
    public const string AssetsProcurement = "Assets.Procurement";
    public const string IDCardLifecycle = "IDCard.Lifecycle";
    public const string ContractsLeave = "Contracts.Leave";
    public const string ContractsAdvance = "Contracts.Advance";
    public const string ContractsHire = "Contracts.Hire";
    public const string ContractsSeparation = "Contracts.Separation";
    public const string ContractsContract = "Contracts.Contract";
    public const string MovementsTransfer = "Movements.Transfer";
    public const string MovementsWorkforce = "Movements.Workforce";
    public const string CoreRecruitment = "Core.Recruitment";
    public const string CoreEstablishment = "Core.Establishment";
    public const string CoreEmployment = "Core.Employment";
    public const string HealthInsurance = "Health.Insurance";
    public const string ComplianceDisciplinary = "Compliance.Disciplinary";
    public const string PayrollPeriod = "Payroll.Period";
    public const string ITTickets = "IT.Tickets";
    public const string ArchiveRetention = "Archive.Retention";
    public const string TrainingLearning = "Training.Learning";
    public const string AnalyticsEtl = "Analytics.ETL";

    public static IReadOnlyList<string> All { get; } =
    [
        AssetsCustody, AssetsProcurement, IDCardLifecycle,
        ContractsLeave, ContractsAdvance, ContractsHire, ContractsSeparation, ContractsContract,
        MovementsTransfer, MovementsWorkforce,
        CoreRecruitment, CoreEstablishment, CoreEmployment,
        HealthInsurance, ComplianceDisciplinary, PayrollPeriod,
        ITTickets, ArchiveRetention, TrainingLearning, AnalyticsEtl
    ];
}
