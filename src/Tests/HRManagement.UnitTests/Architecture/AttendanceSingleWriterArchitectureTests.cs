using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-57 (§2): an attendance day has one writer. No code of HR.Core adds, removes, updates or constructs a
/// <c>DailyAttendanceRecord</c>, runs a bulk statement on the table, or assigns an attendance record's status, outside
/// <c>AttendanceDayWriter</c>. The product source is read as text (every <c>.cs</c> file under <c>src/Services/HRCore</c> but the
/// migrations and the build outputs). The exception list holds the demo seeder only, and only shrinks: a new writer fails the test
/// until it goes through the writer. A planted writer of each kind is the scanner's own negative test.
/// </summary>
public sealed partial class AttendanceSingleWriterArchitectureTests
{
    public const string Writer = "HR.Core.Infrastructure/Services/Attendance/AttendanceDayWriter.cs";

    /// <summary>The files that write an attendance day around the writer, each with why. It only shrinks.</summary>
    public static readonly IReadOnlyDictionary<string, string> Exceptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["HR.Core.Infrastructure/Data/DemoDataSeeder.cs"] = "the demo seed: sixty days of records for a fresh demo database, written before any period exists",
    };

    /// <summary>
    /// Files that read attendance records and assign the <c>Status</c> of something else (never of an attendance record), each
    /// with what it assigns. The scanner cannot tell an attendance record's status from another entity's by text, so such a file is
    /// named here and read by a person.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> OtherStatusAssignments = new Dictionary<string, string>(StringComparer.Ordinal);

    [GeneratedRegex(@"\bDailyAttendanceRecords\s*\.\s*(Add|AddAsync|AddRange|AddRangeAsync|Remove|RemoveRange|Update|UpdateRange|Attach|AttachRange)\s*\(")]
    private static partial Regex SetWrite();

    [GeneratedRegex(@"\bDailyAttendanceRecords\b[^;{}]*\.\s*(ExecuteUpdate|ExecuteUpdateAsync|ExecuteDelete|ExecuteDeleteAsync)\s*\(", RegexOptions.Singleline)]
    private static partial Regex BulkStatement();

    [GeneratedRegex(@"\bnew\s+DailyAttendanceRecord\s*(\(|\{|$)", RegexOptions.Multiline)]
    private static partial Regex Construction();

    [GeneratedRegex(@"\bSet\s*<\s*DailyAttendanceRecord\s*>\s*\(\s*\)\s*\.\s*(Add|AddAsync|AddRange|AddRangeAsync|Remove|RemoveRange|Update|UpdateRange)\s*\(")]
    private static partial Regex GenericSetWrite();

    // The member access on one line: a comment that ends a sentence before an initializer's «Status =» is not an assignment.
    [GeneratedRegex(@"\b\w+(\?|!)?[ \t]*\.[ \t]*Status\s*=(?!=)")]
    private static partial Regex StatusAssignment();

    [GeneratedRegex(@"\bDailyAttendanceRecords?\b")]
    private static partial Regex MentionsAttendance();

    /// <summary>What a source file does to an attendance day, by rule name; empty when it only reads.</summary>
    public static IReadOnlyList<string> Findings(string text)
    {
        var found = new List<string>();
        if (SetWrite().IsMatch(text)) found.Add("writes the DbSet (Add/Remove/Update)");
        if (GenericSetWrite().IsMatch(text)) found.Add("writes Set<DailyAttendanceRecord>()");
        if (BulkStatement().IsMatch(text)) found.Add("runs a bulk statement on the table");
        if (Construction().IsMatch(text)) found.Add("constructs a DailyAttendanceRecord");
        if (MentionsAttendance().IsMatch(text) && StatusAssignment().IsMatch(text)) found.Add("assigns a Status in a file that handles attendance records");
        return found;
    }

    private static IEnumerable<(string Relative, string Text)> HrCoreSources()
    {
        var root = Path.Combine(RepoPaths.Root, "src", "Services", "HRCore");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/Migrations/", StringComparison.Ordinal))
                continue;
            yield return (relative, System.IO.File.ReadAllText(file));
        }
    }

    [Fact]
    public void No_code_of_HrCore_writes_an_attendance_day_outside_the_writer()
    {
        var offenders = HrCoreSources()
            .Select(s => (s.Relative, Found: Findings(s.Text)))
            .Where(s => s.Found.Count > 0 && s.Relative != Writer)
            .Where(s => !(OtherStatusAssignments.ContainsKey(s.Relative) && s.Found.Count == 1 && s.Found[0].StartsWith("assigns a Status", StringComparison.Ordinal)))
            .Select(s => s.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(Exceptions.Keys.Order(StringComparer.Ordinal), offenders);
    }

    [Fact]
    public void The_writer_exists_and_is_the_one_that_writes()
    {
        var writer = HrCoreSources().Single(s => s.Relative == Writer);

        Assert.Contains("writes the DbSet (Add/Remove/Update)", Findings(writer.Text));
        Assert.Contains("constructs a DailyAttendanceRecord", Findings(writer.Text));
        // The writer never saves: its caller saves once.
        Assert.DoesNotContain("SaveChanges", writer.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_exception_lists_name_only_files_that_exist_and_still_need_the_exception()
    {
        var sources = HrCoreSources().ToDictionary(s => s.Relative, s => s.Text, StringComparer.Ordinal);
        foreach (var file in Exceptions.Keys)
        {
            Assert.True(sources.ContainsKey(file), $"{file} is in the exception list and does not exist.");
            Assert.NotEmpty(Findings(sources[file]));
        }

        foreach (var file in OtherStatusAssignments.Keys)
        {
            Assert.True(sources.ContainsKey(file), $"{file} is named for another entity's status and does not exist.");
            Assert.Equal(["assigns a Status in a file that handles attendance records"], Findings(sources[file]));
        }
    }

    [Theory]
    [InlineData("db.DailyAttendanceRecords.Add(record);", "writes the DbSet (Add/Remove/Update)")]
    [InlineData("db.DailyAttendanceRecords\n    .AddRange(records);", "writes the DbSet (Add/Remove/Update)")]
    [InlineData("await db.DailyAttendanceRecords.AddAsync(record, ct);", "writes the DbSet (Add/Remove/Update)")]
    [InlineData("db.DailyAttendanceRecords.Remove(record);", "writes the DbSet (Add/Remove/Update)")]
    [InlineData("db.DailyAttendanceRecords.RemoveRange(records);", "writes the DbSet (Add/Remove/Update)")]
    [InlineData("db.DailyAttendanceRecords.Update(record);", "writes the DbSet (Add/Remove/Update)")]
    [InlineData("db.Set<DailyAttendanceRecord>().Add(record);", "writes Set<DailyAttendanceRecord>()")]
    [InlineData("await db.DailyAttendanceRecords.Where(r => r.EmployeeId == id).ExecuteDeleteAsync(ct);", "runs a bulk statement on the table")]
    [InlineData("await db.DailyAttendanceRecords\n    .Where(r => r.Id == id)\n    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Notes, note), ct);", "runs a bulk statement on the table")]
    [InlineData("db.Add(new DailyAttendanceRecord { EmployeeId = id });", "constructs a DailyAttendanceRecord")]
    [InlineData("var r = await db.DailyAttendanceRecords.FirstAsync(); r.Status = DailyAttendanceStatus.Absent;", "assigns a Status in a file that handles attendance records")]
    [InlineData("DailyAttendanceRecord record = Load(); record!.Status = vm.Status;", "assigns a Status in a file that handles attendance records")]
    public void A_planted_writer_is_found(string source, string rule)
    {
        Assert.Contains(rule, Findings(source));
    }

    [Theory]
    [InlineData("var q = db.DailyAttendanceRecords.AsNoTracking().Where(r => r.Status == DailyAttendanceStatus.Absent);")]
    [InlineData("var record = await db.DailyAttendanceRecords.FirstOrDefaultAsync(x => x.Id == id); if (record.Status == DailyAttendanceStatus.OnLeave) return;")]
    [InlineData("employee.CurrentStatus = EmploymentStatus.OnLeave; var any = db.DailyAttendanceRecords.Any();")]
    [InlineData("var vm = new DailyAttendanceRecordEditViewModel { Id = record.Id, Status = record.Status };")]
    [InlineData("correction.Status = AttendanceCorrectionStatus.Approved;")]
    public void A_read_is_not_a_write(string source)
    {
        Assert.Empty(Findings(source));
    }
}
