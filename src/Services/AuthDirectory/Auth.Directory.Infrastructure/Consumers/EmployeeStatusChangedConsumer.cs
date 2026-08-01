using Auth.Directory.Infrastructure.Data;

using Auth.Directory.Infrastructure.Keycloak;

using HRManagement.MessageContracts.Events;

using HRManagement.ServiceDefaults;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;



namespace Auth.Directory.Infrastructure.Consumers;



public class EmployeeStatusChangedConsumer : IConsumer<IEmployeeStatusChanged>

{

    private readonly AuthDirectoryDbContext _db;

    private readonly IKeycloakUserProvisioningService _keycloak;

    private readonly ILogger<EmployeeStatusChangedConsumer> _logger;



    public EmployeeStatusChangedConsumer(

        AuthDirectoryDbContext db,

        IKeycloakUserProvisioningService keycloak,

        ILogger<EmployeeStatusChangedConsumer> logger)

    {

        _db = db;

        _keycloak = keycloak;

        _logger = logger;

    }



    public async Task Consume(ConsumeContext<IEmployeeStatusChanged> context)

    {

        var m = context.Message;

        _logger.LogInformation(

            "Auth.Directory: EmployeeStatusChanged EmployeeId={EmployeeId}, {OldStatus} -> {NewStatus}",

            m.EmployeeId, m.OldStatus, m.NewStatus);



        var user = await _db.AuthUsers.FirstOrDefaultAsync(u => u.EmployeeId == m.EmployeeId, context.CancellationToken);

        if (user is null)

            return;



        var inactive = m.NewStatus.Contains("Term", StringComparison.OrdinalIgnoreCase)

            || m.NewStatus.Contains("Inactive", StringComparison.OrdinalIgnoreCase)

            || m.NewStatus.Contains("Suspended", StringComparison.OrdinalIgnoreCase)

            || m.NewStatus.Contains("Resign", StringComparison.OrdinalIgnoreCase)

            || m.NewStatus.Contains("Retir", StringComparison.OrdinalIgnoreCase)

            || m.NewStatus.Contains("Deceas", StringComparison.OrdinalIgnoreCase)

            || m.NewStatus.Contains("Anonym", StringComparison.OrdinalIgnoreCase)

            || m.NewStatus.Contains("Merg", StringComparison.OrdinalIgnoreCase)

            || string.Equals(m.NewStatus, "0", StringComparison.Ordinal);



        if (user.IsActive == !inactive)

            return;



        user.IsActive = !inactive;

        await _db.SaveChangesAsync(context.CancellationToken);



        if (!string.IsNullOrEmpty(user.KeycloakUserId))

        {

            var kc = inactive

                ? await _keycloak.TryDisableUserAsync(user.KeycloakUserId, context.CancellationToken)

                : await _keycloak.TryEnableUserAsync(user.KeycloakUserId, context.CancellationToken);



            if (kc.Attempted && !kc.Success)

                _logger.LogWarning("Keycloak {Action} failed for {UserId}: {Detail}",

                    inactive ? "disable" : "enable", user.KeycloakUserId, kc.Detail);

        }

    }

}

