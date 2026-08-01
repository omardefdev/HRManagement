using Payroll.Domain.Entities;

namespace Payroll.Domain.Services.Engine;

public interface IPayrollRule
{
    int Order { get; }
    void Apply(PayrollCalculationContext context, PayrollCalculationResult result);
}

public interface IPayrollEngine
{
    PayrollCalculationResult Calculate(PayrollCalculationContext context);
}
