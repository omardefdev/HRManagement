using System.Globalization;
using System.Runtime.CompilerServices;

namespace HRManagement.UnitTests;

/// <summary>
/// QA-034 (D-QA-11): every test in this assembly runs under ar-SY, the production request culture
/// (HrLocalizationExtensions), which uses the Gregorian calendar. Without it the result depended on the machine:
/// ar-SA (Um Al-Qura) on the owner's machine wrote Hijri years, the invariant culture on Linux CI did not.
/// TestCultureGuardTests fails if this initializer is removed.
/// </summary>
internal static class TestCulture
{
    public const string Name = "ar-SY";

    // CA2255 warns against module initializers in libraries; a test assembly is exactly where a process-wide default
    // culture must be set before any test (or xUnit worker thread) runs.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        var culture = CultureInfo.GetCultureInfo(Name);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
