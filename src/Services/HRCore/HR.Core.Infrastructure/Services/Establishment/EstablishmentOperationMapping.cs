using HR.Core.Domain.Entities;

namespace HR.Core.Infrastructure.Services.Establishment;

public static class EstablishmentOperationMapping
{
    public static EstablishmentOperationType FromChangeType(EstablishmentChangeType changeType) => changeType switch
    {
        EstablishmentChangeType.CreateJobCatalog => EstablishmentOperationType.Create,
        EstablishmentChangeType.BulkCreateJobCatalog => EstablishmentOperationType.Create,
        EstablishmentChangeType.UpdateJobCatalog => EstablishmentOperationType.Update,
        EstablishmentChangeType.DeactivateJobCatalog => EstablishmentOperationType.Deactivate,
        EstablishmentChangeType.CreateFundedPosition => EstablishmentOperationType.PositionCreated,
        EstablishmentChangeType.FreezeFundedPosition => EstablishmentOperationType.Freeze,
        EstablishmentChangeType.UnfreezeFundedPosition => EstablishmentOperationType.Unfreeze,
        _ => EstablishmentOperationType.Update
    };
}
