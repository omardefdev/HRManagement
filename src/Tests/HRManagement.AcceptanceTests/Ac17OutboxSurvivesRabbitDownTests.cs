using System.Net;
using System.Text.RegularExpressions;
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
            1. Select a shortlisted recruitment application and load its protected hire form.
            2. Stop the acceptance RabbitMQ container.
            3. Complete the hire transaction through the gateway while RabbitMQ is unavailable.
            4. Prove HR.Core committed one or more MassTransit outbox rows.
            5. Restart RabbitMQ, prove the outbox drains, and prove Auth.Directory receives the employee.
            """);

        using var http = await AcceptanceHarness.CreateAuthorizedClientAsync(
            persona, correlationId, allowAutoRedirect: false);
        var indexResponse = await http.GetAsync("ui/hr-core/RecruitmentApplications");
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, indexResponse.StatusCode);
        var hireLinkMatch = Regex.Match(
            indexHtml,
            "href=\"[^\"]*RecruitmentApplications/Hire(?:/|\\?id=)([0-9a-fA-F-]{36})[^\"]*\"",
            RegexOptions.IgnoreCase);
        Assert.True(hireLinkMatch.Success, "No shortlisted recruitment application is available for AC-17.");
        var selectedApplicationId = Guid.Parse(hireLinkMatch.Groups[1].Value);
        var hirePath = $"ui/hr-core/RecruitmentApplications/Hire/{selectedApplicationId:D}";

        var hireFormResponse = await http.GetAsync(hirePath);
        var hireHtml = await hireFormResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, hireFormResponse.StatusCode);
        var token = RequireInputValue(hireHtml, "__RequestVerificationToken");
        var applicationId = Guid.Parse(RequireInputValue(hireHtml, "ApplicationId"));

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

            var nationalId = "AC17" + DateTime.UtcNow.ToString("yyMMddHHmmss");
            var form = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["ApplicationId"] = applicationId.ToString("D"),
                ["ApplicantFullName"] = "موظف اختبار الاستمرارية",
                ["AppliedPosition"] = "اختبار صندوق الصادر",
                ["Mode"] = "0",
                ["NationalId"] = nationalId,
                ["FirstName"] = "موظف",
                ["MiddleName"] = "اختبار",
                ["LastName"] = "الاستمرارية",
                ["BirthDate"] = "1995-01-01",
                ["Gender"] = "0",
                ["ExpectedStartDate"] = "2026-08-01",
                ["EmploymentCategory"] = "PERMANENT"
            };
            var hireResponse = await http.PostAsync(
                "ui/hr-core/RecruitmentApplications/Hire",
                new FormUrlEncodedContent(form));
            var hireBody = await hireResponse.Content.ReadAsStringAsync();
            recorder.CaptureHttp("hire-while-rabbit-down", "POST",
                "/ui/hr-core/RecruitmentApplications/Hire", (int)hireResponse.StatusCode,
                new { location = hireResponse.Headers.Location?.ToString(), bodyLength = hireBody.Length });
            Assert.Equal(HttpStatusCode.Redirect, hireResponse.StatusCode);

            var location = hireResponse.Headers.Location?.ToString() ?? string.Empty;
            var employeeMatch = Regex.Match(location, @"employeeId=([0-9a-fA-F-]{36})", RegexOptions.IgnoreCase);
            Assert.True(employeeMatch.Success, $"Hire redirect did not contain employeeId: {location}");
            employeeId = Guid.Parse(employeeMatch.Groups[1].Value);

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
            applicationId,
            employeeId,
            outboxBefore,
            outboxDuring,
            outboxAfter,
            authDirectoryRows = directoryRows
        });
        recorder.WriteAfter(new { rabbitRestarted = true, outboxDrained = true, messageDelivered = true });
        recorder.WriteResult("Pass");
    }

    private static string RequireInputValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $@"name=""{Regex.Escape(name)}""[^>]*value=""([^""]*)""",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"Input '{name}' was not found.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
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
