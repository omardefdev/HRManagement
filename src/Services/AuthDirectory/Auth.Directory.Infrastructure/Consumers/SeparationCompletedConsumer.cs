using Auth.Directory.Infrastructure.Data;

using Auth.Directory.Infrastructure.Keycloak;

using HRManagement.MessageContracts.Events;

using HRManagement.ServiceDefaults;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;



namespace Auth.Directory.Infrastructure.Consumers;



public class SeparationCompletedConsumer : IConsumer<ISeparationCompleted>

{

    private readonly AuthDirectoryDbContext _db;

    private readonly IKeycloakUserProvisioningService _keycloak;

    private readonly ILogger<SeparationCompletedConsumer> _logger;



    public SeparationCompletedConsumer(

        AuthDirectoryDbContext db,

        IKeycloakUserProvisioningService keycloak,

        ILogger<SeparationCompletedConsumer> logger)

    {

        _db = db;

        _keycloak = keycloak;

        _logger = logger;

    }



    public async Task Consume(ConsumeContext<ISeparationCompleted> context)

    {

        var m = context.Message;

        if (!EventSchemaVersion.Supports(m.SchemaVersion, supportedMax: 2, _logger, nameof(ISeparationCompleted)))

            return;



        _logger.LogInformation(

            "Auth.Directory: SeparationCompleted EmployeeId={EmployeeId}, Type={SeparationType}, EffectiveDate={EffectiveDate}",

            m.EmployeeId, m.SeparationType, m.EffectiveDate);



        var user = await _db.AuthUsers.FirstOrDefaultAsync(u => u.EmployeeId == m.EmployeeId, context.CancellationToken);

        if (user is null)

        {

            _logger.LogWarning("Auth.Directory: No AuthUser found for EmployeeId={EmployeeId} during separation", m.EmployeeId);

            return;

        }



        if (!user.IsActive)

            return;



        user.IsActive = false;

        await _db.SaveChangesAsync(context.CancellationToken);



        if (!string.IsNullOrEmpty(user.KeycloakUserId))

        {

            var kc = await _keycloak.TryDisableUserAsync(user.KeycloakUserId, context.CancellationToken);

            if (kc.Attempted && !kc.Success)

                _logger.LogWarning("Keycloak disable failed for {UserId} during separation: {Detail}",

                    user.KeycloakUserId, kc.Detail);

            else

                _logger.LogInformation("Keycloak user {UserId} disabled after separation", user.KeycloakUserId);

        }

    }

}

