namespace HRManagement.Gateway.Models;

public sealed record GatewayModule(
    string Slug,
    string Name,
    string DisplayNameAr,
    string UiPath,
    int DirectPort,
    string HealthPath,
    string Policy,
    string RequiredRole);
