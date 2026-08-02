using System.Diagnostics;

namespace HRManagement.TestSupport;

/// <summary>Cross-platform PowerShell invocation for repo validation script tests.</summary>
public static class RepoScriptRunner
{
    public static string PowerShellExecutable =>
        OperatingSystem.IsWindows() ? "powershell" : "pwsh";

    public static (int ExitCode, string StdOut, string StdErr) RunScript(
        string scriptPath,
        string? extraArguments = null,
        int timeoutMs = 300_000)
    {
        var repoRoot = RepoPaths.Root;
        var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -RepoRoot \"{repoRoot}\"";
        if (!string.IsNullOrWhiteSpace(extraArguments))
            args += " " + extraArguments;

        var psi = new ProcessStartInfo
        {
            FileName = PowerShellExecutable,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot
        };

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(timeoutMs);
        return (proc.ExitCode, stdout, stderr);
    }
}
