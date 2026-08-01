namespace HRManagement.SharedKernel.Entities;

public abstract class AuditableEntity : BaseEntity
{
    public Guid? CreatedBy { get; set; }

    public Guid? UpdatedBy { get; set; }
}
