using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace HRManagement.ServiceDefaults.Seeding;

public enum AcceptanceEmployeeState
{
    Active,
    Probation,
    OnLeave,
    Seconded,
    Terminated,
    Retired
}

public sealed record AcceptanceSeedVolume(
    int LeaveRequests,
    int Advances,
    int Transfers,
    int Secondments,
    int Loans,
    int TrainingEnrollments,
    int IssuedCardsAndRequests,
    int Assets,
    int AssetAssignments,
    int ComplianceCases,
    int DsarRequests,
    int Files,
    int RecruitmentApplications);

public sealed record AcceptanceSite(
    string Code,
    string NameAr,
    string NameEn,
    int OrgUnitCount,
    int EmployeeCount,
    Guid RootOrgUnitId);

public sealed record AcceptanceOrgUnit(
    Guid Id,
    Guid TenantId,
    string Code,
    string NameAr,
    string NameEn,
    Guid? ParentId,
    string MaterializedPath,
    string SiteCode,
    bool IsSiteRoot,
    int SortOrder);

public sealed record AcceptanceEmployee(
    Guid Id,
    Guid TenantId,
    int Index,
    string EmployeeNumber,
    string NationalId,
    string FirstName,
    string? MiddleName,
    string LastName,
    string FullName,
    string Email,
    string Phone,
    string Address,
    DateTime BirthDate,
    DateTime HireDate,
    Guid OrgUnitId,
    string SiteCode,
    string PositionTitle,
    AcceptanceEmployeeState State,
    bool IsFemale,
    bool IsSupervisor)
{
    public bool IsPayable => State is AcceptanceEmployeeState.Active
        or AcceptanceEmployeeState.Probation
        or AcceptanceEmployeeState.OnLeave;

    public bool HasCurrentContract => State is not AcceptanceEmployeeState.Terminated
        and not AcceptanceEmployeeState.Retired;
}

public sealed class AcceptanceAgency
{
    internal AcceptanceAgency(
        Guid id,
        string code,
        string nameAr,
        string nameEn,
        int ordinal,
        IReadOnlyList<AcceptanceSite> sites,
        IReadOnlyList<AcceptanceOrgUnit> orgUnits,
        IReadOnlyList<AcceptanceEmployee> employees,
        AcceptanceSeedVolume volume)
    {
        Id = id;
        Code = code;
        NameAr = nameAr;
        NameEn = nameEn;
        Ordinal = ordinal;
        Sites = sites;
        OrgUnits = orgUnits;
        Employees = employees;
        Volume = volume;
    }

    public Guid Id { get; }
    public string Code { get; }
    public string NameAr { get; }
    public string NameEn { get; }
    public int Ordinal { get; }
    public IReadOnlyList<AcceptanceSite> Sites { get; }
    public IReadOnlyList<AcceptanceOrgUnit> OrgUnits { get; }
    public IReadOnlyList<AcceptanceEmployee> Employees { get; }
    public AcceptanceSeedVolume Volume { get; }

    public int EmployeeCount => Employees.Count;
    public int OrgUnitCount => OrgUnits.Count;
    public int PayableEmployeeCount => Employees.Count(e => e.IsPayable);
    public int CurrentContractCount => Employees.Count(e => e.HasCurrentContract);
    public int SeparationCount => Employees.Count(e => !e.HasCurrentContract);

    public AcceptanceOrgUnit OrgUnitAt(int index) => OrgUnits[index % OrgUnits.Count];
    public AcceptanceEmployee EmployeeAt(int index) => Employees[index % Employees.Count];
}

/// <summary>
/// Canonical, deterministic Acceptance v2 data topology shared by every service.
/// </summary>
public static class AcceptanceSeedCatalog
{
    public const string PackId = "acceptance-v2";
    public const string Version = "2.0.1";
    public static readonly DateTime ReferenceDate = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly Guid AgencyAId = Guid.Parse("a0000001-0000-4000-8000-000000000001");
    public static readonly Guid AgencyBId = Guid.Parse("a0000002-0000-4000-8000-000000000001");
    public static readonly Guid AgencyCId = Guid.Parse("a0000003-0000-4000-8000-000000000001");

    public static readonly Guid AgencyAHqId = Guid.Parse("a0000001-0000-4000-8000-000000000101");
    public static readonly Guid AgencyARifDimashqId = Guid.Parse("a0000001-0000-4000-8000-000000000102");
    public static readonly Guid AgencyARifDimashqHrId = Guid.Parse("a0000001-0000-4000-8000-000000000103");
    public static readonly Guid AgencyAAleppoId = Guid.Parse("a0000001-0000-4000-8000-000000000104");
    public static readonly Guid AgencyBHqId = Guid.Parse("a0000002-0000-4000-8000-000000000101");

    public static readonly Guid LegacyEmployeeBr1Id = Guid.Parse("a0000001-0000-4000-8000-000000000201");
    public static readonly Guid LegacyEmployeeBr1ChildId = Guid.Parse("a0000001-0000-4000-8000-000000000202");
    public static readonly Guid LegacyEmployeeBr2Id = Guid.Parse("a0000001-0000-4000-8000-000000000203");
    public static readonly Guid LegacyEmployeeBId = Guid.Parse("a0000002-0000-4000-8000-000000000201");

    private static readonly string[] DivisionNames =
    [
        "الموارد البشرية", "الشؤون المالية", "الشؤون القانونية", "تقانة المعلومات",
        "التخطيط والتطوير", "الرقابة الداخلية", "الخدمات الإدارية", "التدريب والتأهيل",
        "الرواتب والتعويضات", "الأرشيف والوثائق", "المشتريات والمستودعات", "خدمة المواطنين"
    ];

    private static readonly IReadOnlyList<AcceptanceAgency> AllAgencies = BuildAgencies();
    private static readonly IReadOnlyDictionary<Guid, AcceptanceAgency> ById =
        new ReadOnlyDictionary<Guid, AcceptanceAgency>(AllAgencies.ToDictionary(a => a.Id));
    private static readonly IReadOnlyDictionary<string, AcceptanceAgency> ByCode =
        new ReadOnlyDictionary<string, AcceptanceAgency>(
            AllAgencies.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<AcceptanceAgency> Agencies => AllAgencies;
    public static int TotalEmployees => AllAgencies.Sum(a => a.EmployeeCount);
    public static int TotalOrgUnits => AllAgencies.Sum(a => a.OrgUnitCount);
    public static int TotalPayableEmployees => AllAgencies.Sum(a => a.PayableEmployeeCount);
    public static int TotalCurrentContracts => AllAgencies.Sum(a => a.CurrentContractCount);
    public static int TotalSeparations => AllAgencies.Sum(a => a.SeparationCount);
    public static string ReleaseDigest { get; } = ComputeDigest();

    public static AcceptanceAgency GetAgency(Guid tenantId) =>
        ById.TryGetValue(tenantId, out var agency)
            ? agency
            : throw new KeyNotFoundException($"Unknown acceptance tenant '{tenantId}'.");

    public static AcceptanceAgency GetAgency(string code) =>
        ByCode.TryGetValue(code, out var agency)
            ? agency
            : throw new KeyNotFoundException($"Unknown acceptance tenant '{code}'.");

    public static AcceptanceEmployee GetEmployee(Guid employeeId) =>
        AllAgencies.SelectMany(a => a.Employees).First(e => e.Id == employeeId);

    public static AcceptanceOrgUnit GetOrgUnit(Guid orgUnitId) =>
        AllAgencies.SelectMany(a => a.OrgUnits).First(o => o.Id == orgUnitId);

    public static Guid DeterministicGuid(string tenantCode, string purpose) =>
        CreateGuid($"{PackId}|{tenantCode}|{purpose}");

    private static IReadOnlyList<AcceptanceAgency> BuildAgencies()
    {
        var a = BuildAgency(
            AgencyAId,
            "AGENCY-A",
            "المؤسسة العامة للخدمات الإدارية",
            "General Corporation for Administrative Services",
            1,
            [
                new("HQ", "الإدارة المركزية", "Headquarters", 22, 326, AgencyAHqId),
                new("RIF", "فرع ريف دمشق", "Rural Damascus Branch", 10, 271, AgencyARifDimashqId),
                new("ALEP", "فرع حلب", "Aleppo Branch", 8, 213, AgencyAAleppoId),
                new("HOMS", "فرع حمص", "Homs Branch", 7, 167, CreateGuid("A|site|homs")),
                new("HAMA", "فرع حماة", "Hama Branch", 5, 103, CreateGuid("A|site|hama")),
                new("LAT", "فرع اللاذقية", "Latakia Branch", 5, 79, CreateGuid("A|site|latakia")),
                new("TAR", "فرع طرطوس", "Tartous Branch", 4, 51, CreateGuid("A|site|tartous")),
            ],
            new AcceptanceSeedVolume(314, 130, 90, 34, 22, 315, 930, 330, 235, 67, 18, 84, 12),
            [82, 5, 4, 3, 3, 3]);

        var b = BuildAgency(
            AgencyBId,
            "AGENCY-B",
            "الهيئة العامة للتنمية المؤسسية",
            "Public Authority for Institutional Development",
            2,
            [
                new("HQ", "الإدارة المركزية", "Headquarters", 10, 118, AgencyBHqId),
                new("ALEP", "فرع حلب", "Aleppo Branch", 6, 73, CreateGuid("B|site|aleppo")),
                new("HOMS", "فرع حمص", "Homs Branch", 5, 49, CreateGuid("B|site|homs")),
                new("HAMA", "فرع حماة", "Hama Branch", 3, 31, CreateGuid("B|site|hama")),
            ],
            new AcceptanceSeedVolume(71, 34, 21, 8, 5, 70, 215, 80, 57, 16, 4, 24, 8),
            [86, 4, 3, 2, 3, 2]);

        var c = BuildAgency(
            AgencyCId,
            "AGENCY-C",
            "مؤسسة الخدمات الفنية",
            "Technical Services Corporation",
            3,
            [
                new("HQ", "الإدارة المركزية", "Headquarters", 7, 63, CreateGuid("C|site|hq")),
                new("RIF", "فرع ريف دمشق", "Rural Damascus Branch", 4, 44, CreateGuid("C|site|rif")),
                new("LAT", "فرع اللاذقية", "Latakia Branch", 2, 29, CreateGuid("C|site|latakia")),
            ],
            new AcceptanceSeedVolume(35, 16, 9, 3, 3, 35, 105, 40, 28, 7, 2, 12, 5),
            [88, 3, 3, 2, 2, 2]);

        return Array.AsReadOnly([a, b, c]);
    }

    private static AcceptanceAgency BuildAgency(
        Guid tenantId,
        string code,
        string nameAr,
        string nameEn,
        int ordinal,
        IReadOnlyList<AcceptanceSite> sites,
        AcceptanceSeedVolume volume,
        IReadOnlyList<int> statePercentages)
    {
        var orgUnits = BuildOrgUnits(tenantId, code, sites);
        var employees = BuildEmployees(tenantId, code, ordinal, sites, orgUnits, statePercentages);

        if (employees.Count != sites.Sum(s => s.EmployeeCount))
            throw new InvalidOperationException($"Employee topology mismatch for {code}.");
        if (orgUnits.Count != sites.Sum(s => s.OrgUnitCount))
            throw new InvalidOperationException($"Org-unit topology mismatch for {code}.");

        return new AcceptanceAgency(tenantId, code, nameAr, nameEn, ordinal, sites, orgUnits, employees, volume);
    }

    private static IReadOnlyList<AcceptanceOrgUnit> BuildOrgUnits(
        Guid tenantId,
        string agencyCode,
        IReadOnlyList<AcceptanceSite> sites)
    {
        var result = new List<AcceptanceOrgUnit>(sites.Sum(s => s.OrgUnitCount));
        var hqId = sites[0].RootOrgUnitId;
        var sortOrder = 0;

        foreach (var site in sites)
        {
            var local = new List<AcceptanceOrgUnit>(site.OrgUnitCount);
            var rootParent = site == sites[0] ? (Guid?)null : hqId;
            var rootPath = rootParent is null
                ? $"/{site.RootOrgUnitId:D}/"
                : $"/{hqId:D}/{site.RootOrgUnitId:D}/";

            local.Add(new AcceptanceOrgUnit(
                site.RootOrgUnitId,
                tenantId,
                site.Code,
                site.NameAr,
                site.NameEn,
                rootParent,
                rootPath,
                site.Code,
                true,
                sortOrder++));

            var firstLevelCount = Math.Min(6, site.OrgUnitCount - 1);
            for (var i = 1; i < site.OrgUnitCount; i++)
            {
                var id = agencyCode == "AGENCY-A" && site.Code == "RIF" && i == 1
                    ? AgencyARifDimashqHrId
                    : CreateGuid($"{agencyCode}|org|{site.Code}|{i:D3}");
                var parent = i <= firstLevelCount
                    ? local[0]
                    : local[1 + ((i - firstLevelCount - 1) % firstLevelCount)];
                var division = DivisionNames[(i - 1) % DivisionNames.Length];
                var isDivision = i <= firstLevelCount;
                var nameAr = isDivision ? $"مديرية {division}" : $"قسم {division} {i - firstLevelCount}";
                var nameEn = isDivision ? $"{division} Directorate" : $"{division} Section {i - firstLevelCount}";
                var unitCode = $"{site.Code}-{(isDivision ? "D" : "S")}{i:D2}";
                var path = parent.MaterializedPath + id.ToString("D") + "/";

                local.Add(new AcceptanceOrgUnit(
                    id,
                    tenantId,
                    unitCode,
                    nameAr,
                    nameEn,
                    parent.Id,
                    path,
                    site.Code,
                    false,
                    sortOrder++));
            }

            result.AddRange(local);
        }

        return result.AsReadOnly();
    }

    private static IReadOnlyList<AcceptanceEmployee> BuildEmployees(
        Guid tenantId,
        string agencyCode,
        int agencyOrdinal,
        IReadOnlyList<AcceptanceSite> sites,
        IReadOnlyList<AcceptanceOrgUnit> orgUnits,
        IReadOnlyList<int> statePercentages)
    {
        var result = new List<AcceptanceEmployee>(sites.Sum(s => s.EmployeeCount));
        var globalIndex = 0;

        foreach (var site in sites)
        {
            var siteUnits = orgUnits.Where(o => o.SiteCode == site.Code).ToList();
            var parentIds = siteUnits.Where(o => o.ParentId.HasValue).Select(o => o.ParentId!.Value).ToHashSet();
            var leafUnits = siteUnits.Where(o => !parentIds.Contains(o.Id)).ToArray();

            for (var localIndex = 0; localIndex < site.EmployeeCount; localIndex++)
            {
                var personIndex = agencyOrdinal * 2_000 + globalIndex;
                var person = DemoNameBank.BuildPerson(personIndex);
                var fullName = DemoNameBank.FullName(person.First, person.Middle, person.Last);
                var orgUnit = leafUnits[localIndex % leafUnits.Length];
                var state = ResolveState(globalIndex, statePercentages);
                var id = ResolveLegacyEmployeeId(agencyCode, site.Code, localIndex)
                    ?? CreateGuid($"{agencyCode}|employee|{globalIndex:D5}");
                var employeeNumber = $"{agencyCode[^1]}-{globalIndex + 1:D5}";
                var nationalId = $"99{agencyOrdinal}{globalIndex + 1:D8}";
                var years = 22 + ((globalIndex * 7) % 37);
                var birthDate = ReferenceDate.AddYears(-years).AddDays(-(globalIndex % 330));
                var hireYears = 1 + ((globalIndex * 5) % Math.Max(2, years - 19));
                var hireDate = ReferenceDate.AddYears(-hireYears).AddDays(-(globalIndex % 240));
                var isSupervisor = localIndex == 0 || globalIndex % 25 == 0;

                result.Add(new AcceptanceEmployee(
                    id,
                    tenantId,
                    globalIndex,
                    employeeNumber,
                    nationalId,
                    person.First,
                    person.Middle,
                    person.Last,
                    fullName,
                    $"{agencyCode.ToLowerInvariant()}-{globalIndex + 1:D5}@acceptance.test",
                    DemoNameBank.Phone(agencyOrdinal * 2_000 + globalIndex),
                    DemoNameBank.Address(personIndex),
                    birthDate,
                    hireDate,
                    orgUnit.Id,
                    site.Code,
                    DemoNameBank.JobTitle(globalIndex % DemoNameBank.JobTitles.Length),
                    state,
                    person.IsFemale,
                    isSupervisor));

                globalIndex++;
            }
        }

        return result.AsReadOnly();
    }

    private static AcceptanceEmployeeState ResolveState(int employeeIndex, IReadOnlyList<int> percentages)
    {
        var bucket = employeeIndex % 100;
        var upper = 0;
        for (var i = 0; i < percentages.Count; i++)
        {
            upper += percentages[i];
            if (bucket < upper)
                return (AcceptanceEmployeeState)i;
        }

        return AcceptanceEmployeeState.Retired;
    }

    private static Guid? ResolveLegacyEmployeeId(string agencyCode, string siteCode, int localIndex)
    {
        if (agencyCode == "AGENCY-A" && siteCode == "RIF" && localIndex == 0)
            return LegacyEmployeeBr1Id;
        if (agencyCode == "AGENCY-A" && siteCode == "RIF" && localIndex == 1)
            return LegacyEmployeeBr1ChildId;
        if (agencyCode == "AGENCY-A" && siteCode == "ALEP" && localIndex == 0)
            return LegacyEmployeeBr2Id;
        if (agencyCode == "AGENCY-B" && siteCode == "HQ" && localIndex == 0)
            return LegacyEmployeeBId;
        return null;
    }

    private static string ComputeDigest()
    {
        var canonical = new StringBuilder()
            .Append(PackId).Append('|')
            .Append(Version).Append('|')
            .Append(ReferenceDate.ToString("O")).Append('|');
        foreach (var agency in AllAgencies)
        {
            canonical.Append(agency.Id).Append('|')
                .Append(agency.Code).Append('|')
                .Append(agency.NameAr).Append('|')
                .Append(agency.NameEn).Append('|')
                .Append(agency.Ordinal).Append('|')
                .Append(agency.Volume).Append('|');
            foreach (var site in agency.Sites)
            {
                canonical.Append(site.Code).Append(':')
                    .Append(site.OrgUnitCount).Append(':')
                    .Append(site.EmployeeCount).Append(':')
                    .Append(site.RootOrgUnitId).Append('|');
            }
            foreach (var unit in agency.OrgUnits)
            {
                canonical.Append(unit.Id).Append(':')
                    .Append(unit.ParentId).Append(':')
                    .Append(unit.Code).Append(':')
                    .Append(unit.MaterializedPath).Append('|');
            }
            foreach (var employee in agency.Employees)
            {
                canonical.Append(employee.Id).Append(':')
                    .Append(employee.EmployeeNumber).Append(':')
                    .Append(employee.NationalId).Append(':')
                    .Append(employee.OrgUnitId).Append(':')
                    .Append(employee.State).Append('|');
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static Guid CreateGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"HRM|{PackId}|{value}"));
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
