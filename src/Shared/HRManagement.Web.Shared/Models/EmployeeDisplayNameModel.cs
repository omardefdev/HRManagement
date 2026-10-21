namespace HRManagement.Web.Shared.Models;

/// <summary>
/// Server-resolved employee label with client-side HR.Core lookup fallback via display-names.js.
/// </summary>
public sealed record EmployeeDisplayNameModel(Guid EmployeeId, string? ResolvedName = null);
