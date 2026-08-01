using HRManagement.ServiceDefaults.LegalControl;

using Payroll.Application.Abstractions;



namespace Payroll.Infrastructure;



public sealed class PayrollLegalControlGuard(ILegalControlGuard guard) : IPayrollLegalControlGuard

{

    public string? ValidatePayrollRelease() => guard.ValidatePayrollRelease();



    public bool IsCapabilityAllowed(string requirementId) => guard.IsCapabilityAllowed(requirementId);

}

