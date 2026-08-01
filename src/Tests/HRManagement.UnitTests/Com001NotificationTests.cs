using System.Diagnostics;
using HRManagement.SharedKernel.Notifications;
using HRManagement.ServiceDefaults.Notifications;

namespace HRManagement.UnitTests;
using HRManagement.TestSupport;

public class Com001NotificationTests
{

    private static string RepoRoot
    {
        get
        {
            var script = RepoPaths.Resolve("scripts", "com001-validate.ps1");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, ".."));
        }
    }

    [Fact]
    public async Task Send_delivers_in_app_notification()
    {
        var service = new InAppNotificationService();
        var tenantId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var msg = new NotificationMessage
        {
            TenantId = tenantId,
            RecipientId = recipientId,
            TemplateCode = "leave-approved",
            Channel = NotificationChannel.InApp,
            Subject = "Leave Approved",
            Body = "Your leave has been approved."
        };

        await service.SendAsync(msg);

        var unread = await service.GetUnreadAsync(tenantId, recipientId);
        Assert.Single(unread);
        Assert.Equal(NotificationStatus.Delivered, unread[0].Status);
        Assert.NotNull(unread[0].DeliveredAtUtc);
    }

    [Fact]
    public async Task Duplicate_idempotency_key_does_not_duplicate()
    {
        var service = new InAppNotificationService();
        var tenantId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();

        var msg1 = new NotificationMessage
        {
            TenantId = tenantId,
            RecipientId = recipientId,
            TemplateCode = "contract-created",
            IdempotencyKey = "evt-123"
        };
        var msg2 = new NotificationMessage
        {
            TenantId = tenantId,
            RecipientId = recipientId,
            TemplateCode = "contract-created",
            IdempotencyKey = "evt-123"
        };

        await service.SendAsync(msg1);
        await service.SendAsync(msg2);

        var unread = await service.GetUnreadAsync(tenantId, recipientId);
        Assert.Single(unread);
    }

    [Fact]
    public async Task MarkRead_changes_status()
    {
        var service = new InAppNotificationService();
        var tenantId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var msg = new NotificationMessage
        {
            TenantId = tenantId,
            RecipientId = recipientId,
            TemplateCode = "test"
        };

        await service.SendAsync(msg);
        await service.MarkReadAsync(msg.Id);

        var unread = await service.GetUnreadAsync(tenantId, recipientId);
        Assert.Empty(unread);
    }

    [Fact]
    public void NotificationMessage_defaults()
    {
        var msg = new NotificationMessage();
        Assert.Equal(NotificationChannel.InApp, msg.Channel);
        Assert.Equal(NotificationStatus.Pending, msg.Status);
        Assert.Equal(0, msg.RetryCount);
        Assert.NotEqual(Guid.Empty, msg.Id);
    }

    [Fact]
    public async Task Cross_tenant_isolation()
    {
        var service = new InAppNotificationService();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var recipient = Guid.NewGuid();

        await service.SendAsync(new NotificationMessage
        {
            TenantId = tenant1,
            RecipientId = recipient,
            TemplateCode = "t1-msg"
        });
        await service.SendAsync(new NotificationMessage
        {
            TenantId = tenant2,
            RecipientId = recipient,
            TemplateCode = "t2-msg"
        });

        var t1Unread = await service.GetUnreadAsync(tenant1, recipient);
        var t2Unread = await service.GetUnreadAsync(tenant2, recipient);
        Assert.Single(t1Unread);
        Assert.Single(t2Unread);
        Assert.NotEqual(t1Unread[0].Id, t2Unread[0].Id);
    }

    [Fact]
    public void Validate_script_passes()
    {
        var script = RepoPaths.Resolve("scripts", "com001-validate.ps1");
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -RepoRoot \"{RepoRoot}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        Assert.True(proc.ExitCode == 0, $"validate failed ({proc.ExitCode}): {stdout}\n{stderr}");
        Assert.Contains("PASS", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_exist()
    {
        Assert.True(System.IO.File.Exists(RepoPaths.Resolve("docs", "COM_001_NOTIFICATIONS.md")));
    }
}
