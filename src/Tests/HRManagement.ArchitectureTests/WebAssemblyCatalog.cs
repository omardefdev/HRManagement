using System.Reflection;

namespace HRManagement.ArchitectureTests;

/// <summary>One web assembly: its service key (the name of its ceiling lists), its assembly name and the module policy its filter applies.</summary>
public sealed record WebAssemblyEntry(string Service, string AssemblyName, string? ModulePolicy, bool IsWebAssembly = true);

/// <summary>
/// PLAN-51 (§6, P-8): the explicit list of the fifteen web assemblies, fourteen services and the gateway. A web project on disk that is
/// not listed, or a listed assembly that does not load, fails <see cref="AllWebAssembliesLoadTests"/>; no test here skips one. A sixteenth
/// entry, <c>shared</c>, holds the controllers of ServiceDefaults, which every web assembly serves.
/// </summary>
public static class WebAssemblyCatalog
{
    public static readonly IReadOnlyList<WebAssemblyEntry> Entries =
    [
        new("hr-core", "HR.Core.Web", "Module.HrCore"),
        new("hr-contracts", "HR.Contracts.Web", "Module.HrContracts"),
        new("payroll", "Payroll.Web", "Module.Payroll"),
        new("hr-movements", "HR.Movements.Web", "Module.HrMovements"),
        new("hr-training", "HR.Training.Web", "Module.HrTraining"),
        new("hr-health", "HR.Health.Web", "Module.HrHealth"),
        new("compliance", "Compliance.Web", "Module.Compliance"),
        new("idcard", "IDCard.Web", "Module.IdCard"),
        new("admin-logistics", "Admin.Logistics.Web", "Module.AdminLogistics"),
        new("it-infra", "IT.Infra.Web", "Module.ItInfra"),
        new("records-archive", "Records.Archive.Web", "Module.RecordsArchive"),
        new("dev-analytics", "Dev.Analytics.Web", "Module.DevAnalytics"),
        new("auth-directory", "Auth.Directory.Web", "Module.AuthDirectory"),
        new("file-storage", "File.Storage.Web", "Module.FileStorage"),
        new("gateway", "HRManagement.Gateway", null),
        // The controllers of ServiceDefaults (CultureController) are discovered by every service and the gateway.
        new("shared", "HRManagement.ServiceDefaults", null, IsWebAssembly: false),
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, Assembly>> Loaded = new(() =>
        Entries.ToDictionary(e => e.Service, e => Assembly.Load(e.AssemblyName), StringComparer.Ordinal));

    /// <summary>The loaded assembly of a service; a load failure throws, so the calling test fails.</summary>
    public static Assembly AssemblyOf(string service) => Loaded.Value[service];

    public static WebAssemblyEntry Entry(string service) => Entries.Single(e => e.Service == service);

    /// <summary>Every controller type of the assembly: a public, concrete class deriving from ControllerBase.</summary>
    public static IReadOnlyList<Type> Controllers(string service) =>
        AssemblyOf(service).GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t))
            .Where(t => t.IsVisible && !t.IsDefined(typeof(Microsoft.AspNetCore.Mvc.NonControllerAttribute), inherit: true))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
}
