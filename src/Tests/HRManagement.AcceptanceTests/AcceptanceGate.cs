using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HRManagement.AcceptanceTests;

/// <summary>
/// Marks an acceptance scenario (method or class) that must run: with <c>ACCEPTANCE_REQUIRE_ALL=true</c> a missing
/// precondition fails the test instead of skipping it (PLAN-33, AC-HCM-33b).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class RequiredAcceptanceAttribute : Attribute;

/// <summary>
/// The single way an acceptance test may stop early (PLAN-33 decision 3). A missing precondition is written as
/// <c>result.json = Blocked</c> with its reason and the test is reported as Skipped, never as Passed; with
/// <c>ACCEPTANCE_REQUIRE_ALL=true</c> a <see cref="RequiredAcceptanceAttribute"/> scenario fails instead.
/// </summary>
public static class AcceptanceGate
{
    public const string RequireAllVariable = "ACCEPTANCE_REQUIRE_ALL";

    public static bool RequireAll =>
        string.Equals(Environment.GetEnvironmentVariable(RequireAllVariable), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Skips (or fails, when required) unless <c>RUN_ACCEPTANCE=true</c>. Writes no evidence: nothing ran.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RequireEnabled() =>
        Evaluate(null, AcceptanceEnv.IsEnabled,
            "اختبارات القبول غير مفعّلة", "Set RUN_ACCEPTANCE=true to run acceptance tests.",
            RequireAll, IsRequiredScenario(new StackTrace()));

    /// <summary>As <see cref="RequireEnabled()"/>, recording <c>Blocked</c> in the scenario's evidence folder.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RequireEnabled(EvidenceRecorder recorder) =>
        Evaluate(recorder, AcceptanceEnv.IsEnabled,
            "اختبارات القبول غير مفعّلة", "Set RUN_ACCEPTANCE=true to run acceptance tests.",
            RequireAll, IsRequiredScenario(new StackTrace()));

    /// <summary>
    /// Stops the scenario when <paramref name="condition"/> is false: <c>result.json = Blocked</c>, then Skip, or
    /// <c>Assert.Fail</c> for a required scenario under <c>ACCEPTANCE_REQUIRE_ALL=true</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RequireData(EvidenceRecorder recorder, bool condition, string reasonAr, string reasonEn) =>
        Evaluate(recorder, condition, reasonAr, reasonEn, RequireAll, IsRequiredScenario(new StackTrace()));

    /// <summary>Core decision, with the environment and the attribute lookup passed in (unit-tested).</summary>
    internal static void Evaluate(
        EvidenceRecorder? recorder, bool condition, string reasonAr, string reasonEn, bool requireAll, bool required)
    {
        if (condition)
            return;

        var reason = $"{reasonEn} — {reasonAr}";
        recorder?.WriteResult("Blocked", reason);

        if (requireAll && required)
            Assert.Fail($"Required acceptance scenario blocked ({RequireAllVariable}=true): {reason}");

        Skip.If(true, reason);
    }

    /// <summary>
    /// True when the calling test method, or its class, carries <see cref="RequiredAcceptanceAttribute"/>. Walks the
    /// stack to the first frame whose method is an xUnit test, resolving async state machines to their method.
    /// </summary>
    internal static bool IsRequiredScenario(StackTrace stack)
    {
        foreach (var frame in stack.GetFrames())
        {
            var method = ResolveUserMethod(frame.GetMethod());
            if (method is null || !IsTestMethod(method))
                continue;

            return method.IsDefined(typeof(RequiredAcceptanceAttribute), inherit: true)
                   || method.DeclaringType?.IsDefined(typeof(RequiredAcceptanceAttribute), inherit: true) == true;
        }

        return false;
    }

    private static bool IsTestMethod(MethodBase method) =>
        method.GetCustomAttributes(inherit: true).Any(a => a is FactAttribute);

    private static MethodBase? ResolveUserMethod(MethodBase? method)
    {
        var type = method?.DeclaringType;
        if (method is null || type is null)
            return method;

        // async: <Name>d__N.MoveNext declared inside the test class.
        if (method.Name == "MoveNext" && typeof(IAsyncStateMachine).IsAssignableFrom(type) && type.DeclaringType is { } owner)
        {
            var start = type.Name.IndexOf('<');
            var end = type.Name.IndexOf('>');
            if (start >= 0 && end > start + 1)
            {
                var name = type.Name[(start + 1)..end];
                return owner
                    .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == name && m.IsDefined(typeof(FactAttribute), inherit: true))
                    ?? owner.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == name);
            }
        }

        return method;
    }
}
