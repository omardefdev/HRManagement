using Microsoft.EntityFrameworkCore;

using Payroll.Application.Abstractions;

using Payroll.Domain.Entities;



namespace Payroll.Application.Services;



/// <summary>PERF-001: O(1) query types per partition — bulk prefetch for a slice of employees.</summary>

public sealed class PartitionInputBag

{

    public required IReadOnlyDictionary<Guid, List<AllowanceRecord>> AllowancesByEmployee { get; init; }

    public required IReadOnlyDictionary<Guid, List<DeductionRecord>> DeductionsByEmployee { get; init; }

    public required IReadOnlyDictionary<Guid, List<PayrollAdjustment>> AdjustmentsByEmployee { get; init; }

    public required IReadOnlyDictionary<Guid, List<AbsenceDeduction>> AbsencesByEmployee { get; init; }

    public required HashSet<Guid> ExistingLineEmployeeIds { get; init; }



    public IList<AllowanceRecord> AllowancesFor(Guid employeeId) =>

        AllowancesByEmployee.TryGetValue(employeeId, out var list) ? list : new List<AllowanceRecord>();



    public IList<DeductionRecord> DeductionsFor(Guid employeeId) =>

        DeductionsByEmployee.TryGetValue(employeeId, out var list) ? list : new List<DeductionRecord>();



    public IList<PayrollAdjustment> AdjustmentsFor(Guid employeeId) =>

        AdjustmentsByEmployee.TryGetValue(employeeId, out var list) ? list : new List<PayrollAdjustment>();



    public IList<AbsenceDeduction> AbsencesFor(Guid employeeId) =>

        AbsencesByEmployee.TryGetValue(employeeId, out var list) ? list : new List<AbsenceDeduction>();

}



public static class PayrollPartitionInputLoader

{

    /// <summary>Keyed page size for streaming loads — bounds memory for large national slices.</summary>

    internal const int KeyedPageSize = 2000;



    public static async Task<PartitionInputBag> LoadAsync(

        IPayrollRepository repository,

        IReadOnlyList<Guid> employeeIds,

        PayrollRun run,

        DateOnly periodStart,

        DateOnly periodEnd,

        CancellationToken cancellationToken)

    {

        if (employeeIds.Count == 0)

        {

            return new PartitionInputBag

            {

                AllowancesByEmployee = new Dictionary<Guid, List<AllowanceRecord>>(),

                DeductionsByEmployee = new Dictionary<Guid, List<DeductionRecord>>(),

                AdjustmentsByEmployee = new Dictionary<Guid, List<PayrollAdjustment>>(),

                AbsencesByEmployee = new Dictionary<Guid, List<AbsenceDeduction>>(),

                ExistingLineEmployeeIds = new HashSet<Guid>()

            };

        }



        var idSet = employeeIds as HashSet<Guid> ?? employeeIds.ToHashSet();



        var allowancesByEmployee = await LoadAllowancesAsync(

            repository, idSet, run, cancellationToken);



        // Scheduled instalments must not be visible to a run they are not yet due in; loading the

        // whole repayment plan is what let the first run consume every future instalment.

        var deductionsByEmployee = await LoadKeyedGroupedAsync(

            repository.DeductionRecords

                .AsNoTracking()

                .Where(d => idSet.Contains(d.EmployeeId)

                            && (d.AppliedInRunId == null || d.AppliedInRunId == run.Id)

                            && (d.DueDate == null || d.DueDate <= periodEnd)),

            cancellationToken);



        var adjustmentsByEmployee = await LoadKeyedGroupedAsync(

            repository.PayrollAdjustments

                .AsNoTracking()

                .Where(a => idSet.Contains(a.EmployeeId)
                            && (a.PayrollRunId == null || a.PayrollRunId == run.Id)),

            cancellationToken);



        var absencesByEmployee = await LoadKeyedGroupedAsync(

            repository.AbsenceDeductions

                .AsNoTracking()

                .Where(a => idSet.Contains(a.EmployeeId)

                            && (a.AppliedInRunId == null || a.AppliedInRunId == run.Id)

                            && a.PeriodStart <= periodEnd

                            && a.PeriodEnd >= periodStart),

            cancellationToken);



        var existingLineEmployeeIds = await LoadExistingLineEmployeeIdsAsync(

            repository, run.Id, idSet, cancellationToken);



        return new PartitionInputBag

        {

            AllowancesByEmployee = allowancesByEmployee,

            DeductionsByEmployee = deductionsByEmployee,

            AdjustmentsByEmployee = adjustmentsByEmployee,

            AbsencesByEmployee = absencesByEmployee,

            ExistingLineEmployeeIds = existingLineEmployeeIds

        };

    }



    private static async Task<Dictionary<Guid, List<AllowanceRecord>>> LoadAllowancesAsync(

        IPayrollRepository repository,

        HashSet<Guid> idSet,

        PayrollRun run,

        CancellationToken cancellationToken)

    {

        var result = new Dictionary<Guid, List<AllowanceRecord>>();

        Guid? cursorEmployeeId = null;

        Guid? cursorId = null;



        while (true)

        {

            var pageQuery = repository.AllowanceRecords

                .Include(a => a.AllowanceType)

                .Where(a => idSet.Contains(a.EmployeeId)

                            && a.EffectiveFrom <= run.PeriodEnd

                            && (a.EffectiveTo == null || a.EffectiveTo >= run.PeriodStart))

                .AsNoTracking();



            if (cursorEmployeeId.HasValue)

            {

                var emp = cursorEmployeeId.Value;

                var id = cursorId!.Value;

                pageQuery = pageQuery.Where(a =>

                    a.EmployeeId.CompareTo(emp) > 0

                    || (a.EmployeeId == emp && a.Id.CompareTo(id) > 0));

            }



            var page = await pageQuery

                .OrderBy(a => a.EmployeeId)

                .ThenBy(a => a.Id)

                .Take(KeyedPageSize)

                .ToListAsync(cancellationToken);



            if (page.Count == 0)

                break;



            AppendGrouped(result, page, a => a.EmployeeId);



            var last = page[^1];

            cursorEmployeeId = last.EmployeeId;

            cursorId = last.Id;



            if (page.Count < KeyedPageSize)

                break;

        }



        return result;

    }



    private static async Task<Dictionary<Guid, List<T>>> LoadKeyedGroupedAsync<T>(

        IQueryable<T> baseQuery,

        CancellationToken cancellationToken) where T : class

    {

        var result = new Dictionary<Guid, List<T>>();

        Guid? cursorEmployeeId = null;

        Guid? cursorId = null;



        while (true)

        {

            IQueryable<T> pageQuery = baseQuery;

            if (cursorEmployeeId.HasValue)

            {

                var emp = cursorEmployeeId.Value;

                var id = cursorId!.Value;

                pageQuery = pageQuery.Where(e =>

                    EF.Property<Guid>(e, "EmployeeId").CompareTo(emp) > 0

                    || (EF.Property<Guid>(e, "EmployeeId") == emp

                        && EF.Property<Guid>(e, "Id").CompareTo(id) > 0));

            }



            var page = await pageQuery

                .OrderBy(e => EF.Property<Guid>(e, "EmployeeId"))

                .ThenBy(e => EF.Property<Guid>(e, "Id"))

                .Take(KeyedPageSize)

                .ToListAsync(cancellationToken);



            if (page.Count == 0)

                break;



            AppendGrouped(result, page, ReadEmployeeId<T>);



            var last = page[^1];

            cursorEmployeeId = ReadEmployeeId(last);

            cursorId = ReadEntityId(last);



            if (page.Count < KeyedPageSize)

                break;

        }



        return result;

    }



    private static async Task<HashSet<Guid>> LoadExistingLineEmployeeIdsAsync(

        IPayrollRepository repository,

        Guid payrollRunId,

        HashSet<Guid> idSet,

        CancellationToken cancellationToken)

    {

        var result = new HashSet<Guid>();

        Guid? cursorEmployeeId = null;



        while (true)

        {

            var pageQuery = repository.SalaryLines

                .AsNoTracking()

                .Where(l => l.PayrollRunId == payrollRunId && idSet.Contains(l.EmployeeId));



            if (cursorEmployeeId.HasValue)

            {

                var cursor = cursorEmployeeId.Value;

                pageQuery = pageQuery.Where(l => l.EmployeeId.CompareTo(cursor) > 0);

            }



            var page = await pageQuery

                .OrderBy(l => l.EmployeeId)

                .Select(l => l.EmployeeId)

                .Distinct()

                .Take(KeyedPageSize)

                .ToListAsync(cancellationToken);



            if (page.Count == 0)

                break;



            foreach (var employeeId in page)

                result.Add(employeeId);



            cursorEmployeeId = page[^1];



            if (page.Count < KeyedPageSize)

                break;

        }



        return result;

    }



    private static void AppendGrouped<T>(

        Dictionary<Guid, List<T>> target,

        IReadOnlyList<T> page,

        Func<T, Guid> employeeIdSelector)

    {

        foreach (var item in page)

        {

            var employeeId = employeeIdSelector(item);

            if (!target.TryGetValue(employeeId, out var list))

            {

                list = new List<T>();

                target[employeeId] = list;

            }



            list.Add(item);

        }

    }

    private static Guid ReadEmployeeId<T>(T entity) => ReadEntityProperty<Guid>(entity, "EmployeeId");

    private static Guid ReadEntityId<T>(T entity) => ReadEntityProperty<Guid>(entity, "Id");

    private static TProperty ReadEntityProperty<TProperty>(object entity, string propertyName) =>
        (TProperty)entity.GetType().GetProperty(propertyName)!.GetValue(entity)!;

}


