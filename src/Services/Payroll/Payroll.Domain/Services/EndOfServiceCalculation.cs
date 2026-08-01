using Payroll.Domain.Entities;

using Payroll.Domain.Enums;



namespace Payroll.Domain.Services;



public static class EndOfServiceCalculation

{

    public static decimal CalculateServiceYears(DateTime commencementDate, DateTime separationDate)

    {

        var totalDays = (separationDate - commencementDate).TotalDays;

        return decimal.Round((decimal)(totalDays / 365.25), 2);

    }



    public static decimal CalculateGratuity(

        decimal baseSalary,

        decimal serviceYears,

        PayrollConfiguration? config = null)

    {

        if (serviceYears <= 0 || baseSalary <= 0) return 0;



        var firstRate = config?.EosGratuityRateFirstPeriod ?? 0.5m;

        var threshold = config?.EosGratuityFirstYearsThreshold ?? 5;

        var afterRate = config?.EosGratuityRateAfterThreshold ?? 1.0m;



        decimal gratuity;

        if (serviceYears <= threshold)

        {

            gratuity = baseSalary * firstRate * serviceYears;

        }

        else

        {

            gratuity = baseSalary * firstRate * threshold;

            gratuity += baseSalary * afterRate * (serviceYears - threshold);

        }



        return decimal.Round(gratuity, 2);

    }



    public static decimal CalculateLeaveBalancePayout(

        decimal baseSalary,

        int remainingLeaveDays,

        PayrollConfiguration? config = null)

    {

        if (remainingLeaveDays <= 0 || baseSalary <= 0) return 0;

        var divisor = config?.LeavePayoutDailyDivisor > 0 ? config.LeavePayoutDailyDivisor : 30;

        var dailySalary = baseSalary / divisor;

        return decimal.Round(dailySalary * remainingLeaveDays, 2);

    }



    public static EndOfServiceSettlement CreateSettlement(

        Guid employeeId,

        Guid? separationRecordId,

        DateTime commencementDate,

        DateTime separationDate,

        decimal baseSalary,

        int remainingLeaveDays,

        decimal outstandingAdvances,

        decimal otherCompensation = 0,

        PayrollConfiguration? config = null)

    {

        var serviceYears = CalculateServiceYears(commencementDate, separationDate);

        var gratuity = CalculateGratuity(baseSalary, serviceYears, config);

        var leavePayout = CalculateLeaveBalancePayout(baseSalary, remainingLeaveDays, config);

        var totalDeductions = outstandingAdvances;

        var netSettlement = gratuity + leavePayout + otherCompensation - totalDeductions;



        return new EndOfServiceSettlement

        {

            EmployeeId = employeeId,

            SeparationRecordId = separationRecordId,

            ServiceYears = serviceYears,

            BaseSalary = baseSalary,

            LeaveBalancePayout = leavePayout,

            GratuityAmount = gratuity,

            OtherCompensation = otherCompensation,

            TotalDeductions = totalDeductions,

            NetSettlement = Math.Max(0, netSettlement),

            Status = EosStatus.Draft,

            CalculatedAt = DateTime.UtcNow

        };

    }

}

