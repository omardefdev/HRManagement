using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Data;
using Auth.Directory.Infrastructure.Keycloak;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auth.Directory.Infrastructure.Consumers;

public class EmployeeCreatedConsumer : IConsumer<IEmployeeCreated>
{
    private readonly AuthDirectoryDbContext _db;
    private readonly IKeycloakUserProvisioningService _keycloak;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmployeeCreatedConsumer> _logger;

    public EmployeeCreatedConsumer(
        AuthDirectoryDbContext db,
        IKeycloakUserProvisioningService keycloak,
        IConfiguration configuration,
        ILogger<EmployeeCreatedConsumer> logger)
    {
        _db = db;
        _keycloak = keycloak;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IEmployeeCreated> context)
    {
        var m = context.Message;
        var correlationId = context.Headers.Get<string>(CorrelationIdMiddleware.HeaderName)
            ?? context.CorrelationId?.ToString();
        _logger.LogInformation(
            "Auth.Directory: EmployeeCreated EmployeeId={EmployeeId}, FullName={FullName}, CorrelationId={CorrelationId}",
            m.EmployeeId, m.FullName, correlationId);

        if (await _db.AuthUsers.AnyAsync(u => u.EmployeeId == m.EmployeeId))
            return;

        // Derived from the employee id, never the national identifier. Using the number as a
        // username published it into Keycloak, into every audit log that records a username, and
        // onto any screen that shows who performed an action.
        var username = $"u_{m.EmployeeId:N}";

        var email = $"{m.EmployeeId:N}@directory.local";
        var authUser = new AuthUser
        {
            Username = username,
            Email = email,
            EmployeeId = m.EmployeeId,
            ScopeOrgUnitId = m.PrimaryOrgUnitId,
            TenantId = m.TenantId ?? Guid.Empty,
            PasswordHash = "EXTERNAL_KEYCLOAK",
            IsActive = true
        };
        _db.AuthUsers.Add(authUser);
        await _db.SaveChangesAsync();

        var kc = await _keycloak.TryEnsureUserAsync(
            username, email, m.FullName, m.EmployeeId, m.PrimaryOrgUnitId, m.TenantId, context.CancellationToken);
        if (kc.Attempted && kc.Success && !string.IsNullOrEmpty(kc.Detail))
        {
            authUser.KeycloakUserId = kc.Detail;
            await _db.SaveChangesAsync();

            var defaultRoles = _configuration.GetSection("Keycloak:DirectorySync:DefaultRealmRoles").Get<string[]>()
                ?? ["hr-employee"];
            var rolesResult = await _keycloak.TryAssignRealmRolesAsync(
                authUser.KeycloakUserId, defaultRoles, context.CancellationToken);
            if (rolesResult.Attempted && !rolesResult.Success)
            {
                _logger.LogWarning(
                    "Keycloak default role assign failed for {Username}: {Detail}",
                    username, rolesResult.Detail);
            }
        }
        else if (kc.Attempted && !kc.Success)
        {
            _logger.LogWarning("Keycloak provision failed for {Username}: {Detail}", username, kc.Detail);
        }
    }
}
