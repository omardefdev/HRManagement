namespace HRManagement.ServiceDefaults.Ownership;

public interface IOwnershipProvider
{
    OwnershipOptions Options { get; }
    OwnershipMatrixDocument Matrix { get; }
}

public interface ISustainmentClaimGuard
{
    string? EnsureNational24x7Allowed();
    string? ValidateBudgetShape(IEnumerable<OwnershipBudgetLine> budgetLines);
    string? ValidateVendor(OwnershipVendor vendor);
}
