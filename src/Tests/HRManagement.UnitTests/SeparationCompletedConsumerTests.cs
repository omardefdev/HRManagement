using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Consumers;
using Auth.Directory.Infrastructure.Data;
using Auth.Directory.Infrastructure.Keycloak;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace HRManagement.UnitTests;

public class SeparationCompletedConsumerTests
{
    [Fact]
    public async Task Consume_disables_user_and_calls_keycloak_disable()
    {
        var employeeId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AuthDirectoryDbContext>()
            .UseInMemoryDatabase($"auth-sep-{employeeId}")
            .Options;

        await using var db = new AuthDirectoryDbContext(options);
        var user = new AuthUser
        {
            Username = "test.user",
            Email = "test@local",
            PasswordHash = "EXTERNAL_KEYCLOAK",
            EmployeeId = employeeId,
            KeycloakUserId = "kc-user-1",
            IsActive = true
        };
        db.AuthUsers.Add(user);
        await db.SaveChangesAsync();

        var keycloak = new Mock<IKeycloakUserProvisioningService>();
        keycloak.Setup(k => k.TryDisableUserAsync("kc-user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(KeycloakProvisionResult.Succeeded("kc-user-1"));

        var consumer = new SeparationCompletedConsumer(
            db,
            keycloak.Object,
            Mock.Of<ILogger<SeparationCompletedConsumer>>());

        var message = new Mock<ISeparationCompleted>();
        message.Setup(m => m.EmployeeId).Returns(employeeId);
        message.Setup(m => m.SeparationType).Returns("Resignation");
        message.Setup(m => m.EffectiveDate).Returns(DateTime.UtcNow.Date);

        var context = Mock.Of<ConsumeContext<ISeparationCompleted>>(c =>
            c.Message == message.Object && c.CancellationToken == CancellationToken.None);

        await consumer.Consume(context);

        var updated = await db.AuthUsers.SingleAsync(u => u.EmployeeId == employeeId);
        Assert.False(updated.IsActive);
        keycloak.Verify(k => k.TryDisableUserAsync("kc-user-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
