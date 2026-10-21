using HR.Core.Domain.Entities;

namespace HR.Core.Infrastructure.Services.OrgStructure;

public interface IOrgUnitVersionService
{
    Task AppendVersionAsync(
        OrgUnit unit,
        OrgOperationType operationType,
        DateTime effectiveFrom,
        string? approvedBy = null,
        string? decisionReference = null,
        CancellationToken cancellationToken = default);

    Task<OrgStructureAsOfDto?> ResolveAsOfAsync(Guid orgUnitId, DateTime asOf, CancellationToken cancellationToken = default);

    Task<int> BackfillMissingVersionsAsync(CancellationToken cancellationToken = default);
}

public sealed class OrgStructureAsOfDto
{
    public Guid OrgUnitId { get; init; }
    public DateTime AsOf { get; init; }
    public OrgUnitAsOfSnapshot OrgUnit { get; init; } = new();
    public HeadEmployeeAsOfSnapshot? HeadEmployee { get; init; }
    public IReadOnlyList<FundedPositionAsOfSnapshot> FundedPositions { get; init; } = [];
}

public sealed class OrgUnitAsOfSnapshot
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameAr { get; init; } = string.Empty;
    public string? NameEn { get; init; }
    public Guid? ParentId { get; init; }
    public Guid? HeadEmployeeId { get; init; }
    public OrgOperationType OperationType { get; init; }
    public DateTime ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
}

public sealed class HeadEmployeeAsOfSnapshot
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class FundedPositionAsOfSnapshot
{
    public Guid Id { get; init; }
    public string PositionCode { get; init; } = string.Empty;
    public string GradeCode { get; init; } = string.Empty;
    public FundedPositionStatus Status { get; init; }
    public Guid? OccupantEmployeeId { get; init; }
}
