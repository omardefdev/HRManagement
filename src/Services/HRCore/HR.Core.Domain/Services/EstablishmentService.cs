using HR.Core.Domain.Entities;

namespace HR.Core.Domain.Services;

/// <summary>
/// EST-001: Validates establishment rules — no assignment without a funded,
/// vacant position; no over-capacity; freeze/unfreeze governance.
/// </summary>
public class EstablishmentService
{
    public record AssignResult(bool Success, string? Error = null);

    /// <summary>
    /// Assigns an employee to a funded position. Fails if the position is not
    /// vacant or if occupancy would exceed FTE capacity.
    /// </summary>
    public AssignResult AssignEmployee(FundedPosition position, Guid employeeId)
    {
        if (position.Status == FundedPositionStatus.Frozen)
            return new AssignResult(false, "Cannot assign to a frozen position.");

        if (position.Status == FundedPositionStatus.Occupied)
            return new AssignResult(false, "Position is already occupied.");

        if (position.FTE <= 0)
            return new AssignResult(false, "Position has no FTE capacity.");

        position.OccupantEmployeeId = employeeId;
        position.Status = FundedPositionStatus.Occupied;
        return new AssignResult(true);
    }

    /// <summary>
    /// Vacates a position (e.g. on transfer, separation, or retirement).
    /// </summary>
    public AssignResult VacatePosition(FundedPosition position)
    {
        if (position.Status != FundedPositionStatus.Occupied)
            return new AssignResult(false, "Position is not currently occupied.");

        position.OccupantEmployeeId = null;
        position.Status = FundedPositionStatus.Vacant;
        return new AssignResult(true);
    }

    /// <summary>
    /// Freezes a position, preventing new assignments.
    /// </summary>
    public AssignResult FreezePosition(FundedPosition position, string reason)
    {
        if (position.Status == FundedPositionStatus.Occupied)
            return new AssignResult(false, "Cannot freeze an occupied position. Vacate first.");

        position.Status = FundedPositionStatus.Frozen;
        position.FreezeReason = reason;
        position.FreezeDate = DateTime.UtcNow;
        return new AssignResult(true);
    }

    /// <summary>
    /// Unfreezes a position, making it available for assignment.
    /// </summary>
    public AssignResult UnfreezePosition(FundedPosition position)
    {
        if (position.Status != FundedPositionStatus.Frozen)
            return new AssignResult(false, "Position is not frozen.");

        position.Status = FundedPositionStatus.Vacant;
        position.FreezeReason = null;
        position.FreezeDate = null;
        return new AssignResult(true);
    }

    /// <summary>
    /// Validates that an employee assignment has a funded position backing it.
    /// Returns false if the position is null, frozen, or already occupied by another employee.
    /// </summary>
    public bool HasFundedPosition(FundedPosition? position, Guid employeeId)
    {
        if (position is null)
            return false;
        if (position.Status == FundedPositionStatus.Frozen)
            return false;
        if (position.Status == FundedPositionStatus.Occupied
            && position.OccupantEmployeeId != employeeId)
            return false;
        return true;
    }
}
