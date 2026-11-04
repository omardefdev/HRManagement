using HRManagement.ServiceDefaults;

namespace HRManagement.AcceptanceTests;

/// <summary>AC-17: Rabbit down leaves outbox rows; restart drains without loss.</summary>
public class Ac17OutboxSurvivesRabbitDownTests
{
    [SkippableFact]
    public async Task Outbox_survives_rabbit_stop_and_drains_on_restart()
    {
        AcceptanceEnv.RequireEnabled();
        var correlationId = Guid.NewGuid().ToString("D");
        var persona = AcceptanceHarness.RequirePersona("central-a");
        var (_, _) = await AcceptanceHarness.LoginAsync(persona);
        var recorder = new EvidenceRecorder("AC-17", correlationId);
        recorder.WriteMetadata("AC-17 outbox survives Rabbit down", persona.Id);

        recorder.WriteSteps("""
            1. Prepare a ranked application with an accepted offer (the hire path since PLAN-27).
            2. Stop the acceptance RabbitMQ container.
            3. Complete the hire transaction through the gateway while RabbitMQ is unavailable.
            4. Prove HR.Core committed one or more MassTransit outbox rows.
            5. Restart RabbitMQ, prove the outbox drains, and prove Auth.Directory receives the employee.
            """);

        // PLAN-40: a hire starts from an accepted offer since PLAN-27; the seed has no shortlisted legacy application.
        var (selectedApplicationId, hireAsync) = await AcHcm29HireContractTests.PrepareAcceptedHireAsync("ac17");

        var rabbit = await AcceptanceMessagingHelper.ResolveAcceptanceContainerAsync("rabbitmq");
        var outboxBefore = await AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(
            "hr_core_db", "SELECT COUNT(*) FROM \"OutboxMessage\";");
        var employeeId = Guid.Empty;
        var rabbitStopped = false;
        long outboxDuring;
        try
        {
            var (stopExit, stopOutput) = await AcceptanceMessagingHelper.RunProcessAsync(
                "docker", "stop", rabbit);
            Assert.True(stopExit == 0, stopOutput);
            rabbitStopped = true;

            await hireAsync();
            recorder.CaptureHttp("hire-while-rabbit-down", "POST", "/ui/hr-core/RecruitmentApplications/Hire", 302, new { selectedApplicationId });
            var hired = await AcceptanceMessagingHelper.QueryAcceptancePostgresTextAsync(
                "hr_core_db", $"SELECT \"HiredEmployeeId\" FROM \"RecruitmentApplications\" WHERE \"Id\" = '{selectedApplicationId}';");
            employeeId = Guid.Parse(hired);

            outboxDuring = await PollScalarAsync(
                () => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(
                    "hr_core_db", "SELECT COUNT(*) FROM \"OutboxMessage\";"),
                count => count > outboxBefore,
                TimeSpan.FromSeconds(20));
            Assert.True(outboxDuring > outboxBefore,
                $"Expected an outbox row while RabbitMQ was down. Before={outboxBefore}, during={outboxDuring}");
        }
        finally
        {
            if (rabbitStopped)
            {
                var (startExit, startOutput) = await AcceptanceMessagingHelper.RunProcessAsync(
                    "docker", "start", rabbit);
                Assert.True(startExit == 0, startOutput);
            }
        }

        var outboxAfter = await PollScalarAsync(
            () => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(
                "hr_core_db", "SELECT COUNT(*) FROM \"OutboxMessage\";"),
            count => count <= outboxBefore,
            TimeSpan.FromSeconds(120));
        Assert.True(outboxAfter <= outboxBefore,
            $"HR.Core outbox did not drain. Before={outboxBefore}, after={outboxAfter}");

        var directoryRows = await PollScalarAsync(
            () => AcceptanceMessagingHelper.QueryAcceptancePostgresScalarAsync(
                "auth_directory_db",
                $"SELECT COUNT(*) FROM \"AuthUsers\" WHERE \"EmployeeId\" = '{employeeId:D}';"),
            count => count == 1,
            TimeSpan.FromSeconds(120));
        Assert.Equal(1, directoryRows);

        recorder.WriteDbChecks(new
        {
            applicationId = selectedApplicationId,
            employeeId,
            outboxBefore,
            outboxDuring,
            outboxAfter,
            authDirectoryRows = directoryRows
        });
        recorder.WriteAfter(new { rabbitRestarted = true, outboxDrained = true, messageDelivered = true });
        recorder.WriteResult("Pass");
    }

    private static async Task<long> PollScalarAsync(
        Func<Task<long>> read,
        Func<long, bool> completed,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var value = await read();
        while (!completed(value) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(1_000);
            value = await read();
        }
        return value;
    }
}
