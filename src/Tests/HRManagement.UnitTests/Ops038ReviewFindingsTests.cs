using System.Diagnostics;
using System.Text.RegularExpressions;
using HRManagement.TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-38 P8 review findings that live in shell scripts:
/// F3 — the bootstrap hands each broker password to jq on stdin, never as an argument;
/// F4 — the k8s migration path (scripts/run-migrations.sh, shipped in hr-migrate) passes passwords through PGPASSWORD,
/// never inside a --connection or psql argument;
/// F6 — remote-deploy.sh refuses pool sizes from .env whose total reaches 90 % of max_connections.
/// </summary>
public sealed class Ops038ReviewFindingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ops038-review-" + Guid.NewGuid().ToString("N"));

    public Ops038ReviewFindingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static string Script(string relative) =>
        System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Bootstrap_never_passes_a_secret_as_a_jq_argument()
    {
        var text = Script("deploy/bootstrap/bootstrap.sh");
        Assert.DoesNotMatch(new Regex(@"jq[^\n|]*\$\(secret", RegexOptions.CultureInvariant), text);
        Assert.Contains("secret \"$pass_var\" | jq -Rs", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_script_keeps_passwords_off_every_command_line()
    {
        var text = Script("scripts/run-migrations.sh");
        Assert.DoesNotMatch(new Regex(@"--connection ""\$\{(conn_val|migrate_conn)\}""", RegexOptions.CultureInvariant), text);
        Assert.DoesNotMatch(new Regex(@"psql ""\$\{(conn|LOCK_CONN)\}""", RegexOptions.CultureInvariant), text);
        Assert.Equal(4, Regex.Matches(text, @"PGPASSWORD=""\$\{CONN_PW\}""", RegexOptions.CultureInvariant).Count);
    }

    [Fact]
    public void Split_conn_removes_the_password_and_builds_libpq_keywords()
    {
        var (code, output) = Bash(
            "source <(sed -n '/^split_conn() {/,/^}/p' scripts/run-migrations.sh)\n" +
            "split_conn 'Host=postgresql; Port=5432;Database=payroll_db;Username=payroll_migrator;Password=a=b_c;SSL Mode=VerifyFull;Maximum Pool Size=10'\n" +
            "printf 'NOPW=%s\\nINFO=%s\\nPWLEN=%s\\n' \"$CONN_NO_PW\" \"$CONN_INFO\" \"${#CONN_PW}\"\n" +
            "split_conn ''\nprintf 'EMPTY=[%s][%s]\\n' \"$CONN_NO_PW\" \"$CONN_INFO\"\n");
        Assert.True(code == 0, output);
        Assert.Contains("NOPW=Host=postgresql; Port=5432;Database=payroll_db;Username=payroll_migrator;SSL Mode=VerifyFull;Maximum Pool Size=10;", output, StringComparison.Ordinal);
        Assert.Contains("INFO=host=postgresql port=5432 dbname=payroll_db user=payroll_migrator sslmode=verify-full", output, StringComparison.Ordinal);
        Assert.Contains("PWLEN=5", output, StringComparison.Ordinal);
        Assert.Contains("EMPTY=[][]", output, StringComparison.Ordinal);
        Assert.DoesNotContain("a=b_c", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("HRM_PG_POOL_MAX=12", true)]   // 14*12 + 20 + 20 + 5 = 213 < 225
    [InlineData("HRM_PG_POOL_MAX=13", false)]  // 227: refused
    [InlineData("HRM_PG_POOL_MAX=14", false)]
    [InlineData("HRM_PG_POOL_MAX_HR_CORE=60", false)]
    [InlineData("HRM_PG_POOL_MAX=ten", false)]
    public void Deploy_refuses_pool_sizes_that_reach_ninety_percent_of_max_connections(string extra, bool accepted)
    {
        // 14 pools + HR.Core + Keycloak 20 + reserve 5 against 90 % of max_connections 250 (= 225).
        var release = Path.Combine(_dir, "release");
        Directory.CreateDirectory(Path.Combine(release, "deploy", "vps"));
        System.IO.File.Copy(Path.Combine(RepoPaths.Root, "docker-compose.vps.yml"), Path.Combine(release, "docker-compose.vps.yml"));
        var example = Path.Combine(release, "deploy", "vps", ".env.vps.example");
        System.IO.File.Copy(Path.Combine(RepoPaths.Root, "deploy", "vps", ".env.vps.example"), example);

        // Every example key with a long dummy value (no real secret), demo mode, a commit-like tag.
        var env = System.IO.File.ReadAllLines(example)
            .Where(l => Regex.IsMatch(l, "^[A-Z][A-Z0-9_]*=", RegexOptions.CultureInvariant))
            .Select(l => l[..(l.IndexOf('=') + 1)])
            .Select(k => k switch
            {
                "IMAGE_TAG=" => k + "0123456789ab",
                "HRM_DEPLOYMENT_MODE=" => k + "demo",
                "HRM_DB_POSTURE=" => k + "demo",
                "HRM_MQ_POSTURE=" => k + "least-privilege",
                _ => k + "dummy-value-for-the-check-0123456789abcdef",
            })
            .ToList();
        if (extra.Length > 0)
            env.Add(extra);
        var envFile = Path.Combine(_dir, ".env");
        System.IO.File.WriteAllText(envFile, string.Join('\n', env) + "\n");
        if (!OperatingSystem.IsWindows())
            System.IO.File.SetUnixFileMode(envFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); // check_env wants 600

        var (code, output) = Bash(
            "log() { echo \"$*\"; }; die() { echo \"DIE: $*\"; exit 1; }\n" +
            "source <(sed -n '/^env_value() {/,/^}/p;/^check_env() {/,/^}/p' deploy/vps/remote-deploy.sh)\n" +
            $"ENV_FILE='{ToBash(envFile)}'; ALLOW_EMPTY=''\n" +
            $"check_env '{ToBash(example)}'\n");

        if (accepted)
        {
            Assert.True(code == 0, output);
            Assert.Contains("env check:", output, StringComparison.Ordinal);
        }
        else
        {
            Assert.NotEqual(0, code);
            Assert.Matches(new Regex("DIE: (connection pools: [0-9]+ of max_connections 250|HRM_PG_POOL_MAX and HRM_PG_POOL_MAX_HR_CORE must be whole numbers)", RegexOptions.CultureInvariant), output);
        }
    }

    private (int Code, string Output) Bash(string body)
    {
        var script = Path.Combine(_dir, "run.sh");
        System.IO.File.WriteAllText(script, "set -euo pipefail\n" + body.Replace("\r\n", "\n"));
        var psi = new ProcessStartInfo(BashPath(), $"\"{ToBash(script)}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoPaths.Root,
        };
        psi.Environment["MSYS_NO_PATHCONV"] = "1";
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "bash timed out");
        return (process.ExitCode, output);
    }

    private static string ToBash(string path) => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    private static string BashPath()
    {
        if (!OperatingSystem.IsWindows())
            return "bash";
        var psi = new ProcessStartInfo("git", "--exec-path") { RedirectStandardOutput = true, UseShellExecute = false };
        using var git = Process.Start(psi)!;
        var exec = git.StandardOutput.ReadToEnd().Trim();
        git.WaitForExit();
        // <git>/mingw64/libexec/git-core -> <git>/bin/bash.exe
        var root = Directory.GetParent(exec)!.Parent!.Parent!.FullName;
        return Path.Combine(root, "bin", "bash.exe");
    }
}
