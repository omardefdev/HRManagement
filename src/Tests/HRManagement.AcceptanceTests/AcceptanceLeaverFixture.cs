using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;

namespace HRManagement.AcceptanceTests;

/// <summary>A new employee copied from the leaver template for one scenario: his id, his contract's, his unit, and a label for the evidence.</summary>
internal sealed record LeaverCopy(Guid EmployeeId, Guid ContractId, Guid OrgUnitId, string Label);

/// <summary>
/// PLAN-60 (P-12): the leaver fixture. Each scenario that separates someone takes a new copy of the template persona <c>a-leaver</c>
/// (agency A, HQ), made by the screens alone — the URL and the token each page renders — and never the template itself:
/// <list type="bullet">
/// <item>an HR.Core employee by <c>Employees/Create</c> (<c>central-a</c>), in the template's unit, with a unique name marked
/// AC-SEP-60 and a hire date before the current month;</item>
/// <item>his service regime classified by the payroll classes' stated precondition
/// (<see cref="AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync"/>): <c>Employees/Create</c> opens no regime exception, so
/// <c>ServiceRegimeExceptions/Resolve</c> has nothing to resolve, and the execution validates the employee's active regime;</item>
/// <item>a contract by <c>Contracts/Create</c> (<c>a-contracts-maker</c>), started before the current month, no probation — left
/// <b>Pending</b>: no screen sets a contract's grade or salary, and an activation without them leaves Payroll's PAY029-NOSAL review hold
/// in agency A, which AC-PAY-58's isolation rule forbids. A pending contract has no pay profile and no payroll footprint; the execution
/// ends it at the effective date as it ends an active one.</item>
/// </list>
/// It waits until HR.Contracts holds the copy's unit (the org scope of the separation pages reads it) and returns the ids. PLAN-61..64
/// extend it (PLAN-64's settlement, with a paid profile, when a screen sets the salary), and never change the shared persona.
/// </summary>
internal static class AcceptanceLeaverFixture
{
    public const string TemplatePersona = "a-leaver";
    public const string Marker = "AC-SEP-60";
    public const string ContractTypeCode = "AGENCY-A-ACC-LAW50-PERM";

    private static readonly string TenantA = AcceptanceSeedIds.AgencyAId.ToString("D");
    private static readonly TimeSpan SnapshotWindow = TimeSpan.FromSeconds(60);

    /// <summary>The template's employee (the persona's seeded employee); never separated, never changed.</summary>
    public static Guid TemplateEmployee(string template = TemplatePersona) =>
        AcceptancePersonaCatalog.Get(template).Employee?.Id
        ?? throw new InvalidOperationException($"Persona {template} has no seeded employee.");

    /// <summary>The template's rows (HR.Core's employee and employment records, HR.Contracts' contracts and records), as one checksum.</summary>
    public static async Task<string> TemplateFingerprintAsync(string template = TemplatePersona)
    {
        var employee = TemplateEmployee(template).ToString("D");
        var core = await SeparationJourney.ReadAsync(SeparationJourney.Core,
            $"SELECT md5(COALESCE((SELECT string_agg(row_to_json(e)::text, '') FROM \"Employees\" e WHERE e.\"Id\"='{employee}'),'') || " +
            $"COALESCE((SELECT string_agg(row_to_json(r)::text, '' ORDER BY r.\"Id\") FROM \"EmploymentRecords\" r WHERE r.\"EmployeeId\"='{employee}'),''));");
        var contracts = await SeparationJourney.ReadAsync(SeparationJourney.Contracts,
            $"SELECT md5(COALESCE((SELECT string_agg(row_to_json(c)::text, '' ORDER BY c.\"Id\") FROM \"Contracts\" c WHERE c.\"EmployeeId\"='{employee}'),'') || " +
            $"COALESCE((SELECT string_agg(row_to_json(s)::text, '' ORDER BY s.\"Id\") FROM \"SeparationRecords\" s WHERE s.\"EmployeeId\"='{employee}'),''));");
        return $"{core}|{contracts}";
    }

    public static async Task<LeaverCopy> CreateAsync(string template, EvidenceRecorder recorder)
    {
        var templateEmployee = TemplateEmployee(template);
        var unitText = await SeparationJourney.ReadAsync(SeparationJourney.Core,
            $"SELECT COALESCE(\"PrimaryOrgUnitId\"::text,'') FROM \"Employees\" WHERE \"Id\"='{templateEmployee:D}';");
        AcceptanceGate.RequireData(recorder, Guid.TryParse(unitText, out var unit),
            $"الموظف القالب للشخصية {template} غير موجود أو بلا وحدة", $"The template employee of {template} is missing or has no unit (scripts/acceptance-reset.ps1).");
        var typeText = await SeparationJourney.ReadAsync(SeparationJourney.Contracts,
            $"SELECT \"Id\" FROM \"ContractTypes\" WHERE \"Code\"='{ContractTypeCode}' AND \"TenantId\"='{TenantA}' LIMIT 1;");
        AcceptanceGate.RequireData(recorder, Guid.TryParse(typeText, out var contractType),
            $"نوع العقد {ContractTypeCode} غير موجود", $"Contract type {ContractTypeCode} is missing (scripts/acceptance-reset.ps1).");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var label = $"{Marker}-{suffix}";
        var today = SeparationJourney.TodayInDamascus();
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);

        // 1. The employee, by HR.Core's form (central-a, the agency's HR): the redirect names his id.
        Guid employee;
        using (var central = await ScreenSession.OpenAsync("central-a", recorder))
        {
            var created = await central.PostAsync($"copy-employee-{suffix}", "ui/hr-core/Employees/Create", "ui/hr-core/Employees/Create",
                new Dictionary<string, string>
                {
                    ["NationalId"] = NationalId(),
                    ["FirstName"] = "مغادر",
                    ["MiddleName"] = "نسخة",
                    ["LastName"] = label,
                    ["BirthDate"] = "1990-03-15",
                    ["Gender"] = "Male",
                    ["MaritalStatus"] = "Single",
                    ["PrimaryOrgUnitId"] = unit.ToString("D"),
                    ["HireDate"] = SeparationJourney.Iso(start)
                });
            var id = Regex.Match(created.Location ?? "", @"/Employees/Details/([0-9a-fA-F-]{36})");
            Assert.True(id.Success, $"Employees/Create did not redirect to the new employee: {(int)created.Status} {created.Alerts} {SeparationJourney.ValidationErrors(created.Html)}");
            employee = Guid.Parse(id.Groups[1].Value);
        }

        // 2. His regime (the stated precondition of the payroll classes; the regime gate is not what AC-SEP-60 proves).
        var (records, exceptions) = await AcceptancePayrollRegime.ClassifyRecordsAddedSinceTheSeedAsync(TenantA);

        // 3. His contract, by HR.Contracts' form (the maker): Pending, started before the current month, no probation.
        using (var maker = await ScreenSession.OpenAsync("a-contracts-maker", recorder))
        {
            var created = await maker.PostAsync($"copy-contract-{suffix}", "ui/hr-contracts/Contracts/Create", "ui/hr-contracts/Contracts/Create",
                new Dictionary<string, string>
                {
                    ["EmployeeId"] = employee.ToString("D"),
                    ["ContractTypeId"] = contractType.ToString("D"),
                    ["StartDate"] = SeparationJourney.Iso(start),
                    ["ProbationMonths"] = "0",
                    ["Terms"] = $"{Marker}: عقد نسخة اختبار"
                });
            Assert.True((int)created.Status is >= 300 and < 400, $"Contracts/Create: {(int)created.Status} {SeparationJourney.ValidationErrors(created.Html)}");
        }

        var contractText = await SeparationJourney.ReadAsync(SeparationJourney.Contracts,
            $"SELECT \"Id\" FROM \"Contracts\" WHERE \"EmployeeId\"='{employee:D}' AND \"Status\"='Pending' ORDER BY \"CreatedAt\" DESC LIMIT 1;");
        Assert.True(Guid.TryParse(contractText, out var contract), $"No pending contract for {label}.");

        // 4. HR.Contracts holds his unit (IEmployeeCreated): the separation pages' org scope reads it. A read, within a minute.
        var deadline = DateTime.UtcNow + SnapshotWindow;
        long snapshot;
        do
        {
            snapshot = await SeparationJourney.CountAsync(SeparationJourney.Contracts,
                $"SELECT count(*) FROM \"EmployeeOrgUnitSnapshots\" WHERE \"EmployeeId\"='{employee:D}' AND \"PrimaryOrgUnitId\"='{unit:D}';");
            if (snapshot > 0)
                break;
            await Task.Delay(TimeSpan.FromSeconds(1));
        } while (DateTime.UtcNow < deadline);

        Assert.True(snapshot > 0, $"HR.Contracts does not hold {label}'s unit after {SnapshotWindow.TotalSeconds:0}s.");
        recorder.CaptureHttp($"copy-{suffix}", "FIXTURE", "/fixture/leaver-copy", 200, new
        {
            template,
            employee,
            contract,
            unit,
            hireDate = SeparationJourney.Iso(start),
            regimeClassified = new { records, exceptions }
        });
        return new LeaverCopy(employee, contract, unit, label);
    }

    /// <summary>A national number no seeded person has (the seed's begin with 99); unique per agency by its hash.</summary>
    private static string NationalId() =>
        "97" + string.Concat(Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(0, 10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
