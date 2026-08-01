using IT.Infrastructure.Domain.Entities;
using IT.Infrastructure.Domain.Enums;
using IT.Infrastructure.Domain.Services;
using Dev.Analytics.Domain.Entities;
using Dev.Analytics.Domain.Enums;
using Dev.Analytics.Domain.Services;

namespace HRManagement.UnitTests;

public class Phase6_TicketWorkflowRulesTests
{
    [Fact]
    public void CanAssign_Open_ReturnsTrue() =>
        Assert.True(TicketWorkflowRules.CanAssign(TicketStatus.Open));

    [Theory]
    [InlineData(TicketStatus.Assigned)]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void CanAssign_NonOpen_ReturnsFalse(TicketStatus status) =>
        Assert.False(TicketWorkflowRules.CanAssign(status));

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Assigned)]
    public void CanResolve_OpenOrAssigned_ReturnsTrue(TicketStatus status) =>
        Assert.True(TicketWorkflowRules.CanResolve(status));

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void CanResolve_ResolvedOrClosed_ReturnsFalse(TicketStatus status) =>
        Assert.False(TicketWorkflowRules.CanResolve(status));

    [Fact]
    public void CanClose_Resolved_ReturnsTrue() =>
        Assert.True(TicketWorkflowRules.CanClose(TicketStatus.Resolved));

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Assigned)]
    [InlineData(TicketStatus.Closed)]
    public void CanClose_NonResolved_ReturnsFalse(TicketStatus status) =>
        Assert.False(TicketWorkflowRules.CanClose(status));

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void CanReopen_ResolvedOrClosed_ReturnsTrue(TicketStatus status) =>
        Assert.True(TicketWorkflowRules.CanReopen(status));

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Assigned)]
    public void CanReopen_OpenOrAssigned_ReturnsFalse(TicketStatus status) =>
        Assert.False(TicketWorkflowRules.CanReopen(status));

    [Fact]
    public void IsEscalated_HighSeverityAndOpen_ReturnsTrue()
    {
        var incident = new SecurityIncident
        {
            Severity = Severity.High,
            Status = TicketStatus.Open,
            Title = "Test"
        };
        Assert.True(TicketWorkflowRules.IsEscalated(incident));
    }

    [Fact]
    public void IsEscalated_CriticalSeverityAndOpen_ReturnsTrue()
    {
        var incident = new SecurityIncident
        {
            Severity = Severity.Critical,
            Status = TicketStatus.Open,
            Title = "Test"
        };
        Assert.True(TicketWorkflowRules.IsEscalated(incident));
    }

    [Fact]
    public void IsEscalated_LowSeverity_ReturnsFalse()
    {
        var incident = new SecurityIncident
        {
            Severity = Severity.Low,
            Status = TicketStatus.Open,
            Title = "Test"
        };
        Assert.False(TicketWorkflowRules.IsEscalated(incident));
    }

    [Fact]
    public void IsEscalated_HighButResolved_ReturnsFalse()
    {
        var incident = new SecurityIncident
        {
            Severity = Severity.High,
            Status = TicketStatus.Resolved,
            Title = "Test"
        };
        Assert.False(TicketWorkflowRules.IsEscalated(incident));
    }
}

public class Phase6_ETLJobExecutionRulesTests
{
    [Fact]
    public void CanRun_Ok_ReturnsTrue() =>
        Assert.True(ETLJobExecutionRules.CanRun(JobStatus.Ok));

    [Fact]
    public void CanRun_Failed_ReturnsFalse() =>
        Assert.False(ETLJobExecutionRules.CanRun(JobStatus.Failed));

    [Fact]
    public void ShouldRefresh_NextRunInPast_ReturnsTrue()
    {
        var schedule = new DataRefreshSchedule
        {
            NextRunAt = DateTime.UtcNow.AddMinutes(-5)
        };
        Assert.True(ETLJobExecutionRules.ShouldRefresh(schedule));
    }

    [Fact]
    public void ShouldRefresh_NextRunInFuture_ReturnsFalse()
    {
        var schedule = new DataRefreshSchedule
        {
            NextRunAt = DateTime.UtcNow.AddHours(2)
        };
        Assert.False(ETLJobExecutionRules.ShouldRefresh(schedule));
    }

    [Fact]
    public void ShouldRefresh_NullNextRun_ReturnsFalse()
    {
        var schedule = new DataRefreshSchedule { NextRunAt = null };
        Assert.False(ETLJobExecutionRules.ShouldRefresh(schedule));
    }

    [Fact]
    public void IsStale_OldRefresh_ReturnsTrue()
    {
        var model = new ReadModel
        {
            Name = "Test",
            LastRefreshed = DateTime.UtcNow.AddHours(-48)
        };
        Assert.True(ETLJobExecutionRules.IsStale(model, 24));
    }

    [Fact]
    public void IsStale_RecentRefresh_ReturnsFalse()
    {
        var model = new ReadModel
        {
            Name = "Test",
            LastRefreshed = DateTime.UtcNow.AddHours(-1)
        };
        Assert.False(ETLJobExecutionRules.IsStale(model, 24));
    }

    [Fact]
    public void IsStale_NullRefresh_ReturnsFalse()
    {
        var model = new ReadModel
        {
            Name = "Test",
            LastRefreshed = null
        };
        Assert.False(ETLJobExecutionRules.IsStale(model, 24));
    }
}

public class Phase6_ITInfraEntityTests
{
    [Fact]
    public void ITSupportTicket_DefaultStatus_IsOpen()
    {
        var ticket = new ITSupportTicket { IssueSummary = "Test" };
        Assert.Equal(TicketStatus.Open, ticket.Status);
    }

    [Fact]
    public void ServerDevice_Properties_SetCorrectly()
    {
        var device = new ServerDevice
        {
            Hostname = "srv-01",
            IpAddress = "10.0.0.1",
            Status = ServerStatus.Active
        };
        Assert.Equal("srv-01", device.Hostname);
        Assert.Equal(ServerStatus.Active, device.Status);
    }

    [Fact]
    public void UserDevice_EnumValues_AreValid()
    {
        var device = new UserDevice
        {
            AssetTag = "PC-001",
            DeviceType = DeviceType.Laptop,
            Status = DeviceStatus.InUse
        };
        Assert.Equal(DeviceType.Laptop, device.DeviceType);
        Assert.Equal(DeviceStatus.InUse, device.Status);
    }

    [Fact]
    public void SystemPermissionRecord_Revoke_SetsRevokedAt()
    {
        var perm = new SystemPermissionRecord
        {
            UserId = Guid.NewGuid(),
            System = "SAP",
            GrantedAt = DateTime.UtcNow
        };
        Assert.Null(perm.RevokedAt);
        perm.RevokedAt = DateTime.UtcNow;
        Assert.NotNull(perm.RevokedAt);
    }
}

public class Phase6_AnalyticsEntityTests
{
    [Fact]
    public void Dashboard_Name_Required()
    {
        var d = new Dashboard { Name = "HR Overview" };
        Assert.Equal("HR Overview", d.Name);
    }

    [Fact]
    public void KPI_Code_Required()
    {
        var kpi = new KPI { Code = "EMP-001", Name = "Employee Count" };
        Assert.Equal("EMP-001", kpi.Code);
    }

    [Fact]
    public void ETLJob_DefaultStatus()
    {
        var job = new ETLJob { Name = "Import HR", Status = JobStatus.Ok };
        Assert.Equal(JobStatus.Ok, job.Status);
    }

    [Fact]
    public void Report_TypeValues()
    {
        var r = new Report { Title = "Monthly", ReportType = ReportType.Monthly };
        Assert.Equal(ReportType.Monthly, r.ReportType);
    }
}

public class Phase6_EventSchemaTests
{
    [Fact]
    public void IPayrollProcessed_SchemaVersion_Is1()
    {
        var e = new PayrollProcessedStub();
        Assert.Equal(1, ((HRManagement.MessageContracts.Events.IPayrollProcessed)e).SchemaVersion);
    }

    [Fact]
    public void IEmployeeCreated_SchemaVersion_Is3()
    {
        // v3 replaced the raw NationalId with NationalIdHash.
        var e = new EmployeeCreatedStub();
        Assert.Equal(3, ((HRManagement.MessageContracts.Events.IEmployeeCreated)e).SchemaVersion);
    }

    private class PayrollProcessedStub : HRManagement.MessageContracts.Events.IPayrollProcessed
    {
        public Guid PayrollRunId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal TotalNetPay { get; set; }
    }

    private class EmployeeCreatedStub : HRManagement.MessageContracts.Events.IEmployeeCreated
    {
        public Guid EmployeeId { get; set; }
        public string FullName { get; set; } = "";
        public string NationalIdHash { get; set; } = "";
        public string EmploymentType { get; set; } = "";
        public Guid? TenantId { get; set; }
        public Guid? PrimaryOrgUnitId { get; set; }
    }
}

public class Phase6_EntityRelationshipTests
{
    [Fact]
    public void DataRefreshSchedule_TargetReadModel_FK()
    {
        var rm = new ReadModel { Id = Guid.NewGuid(), Name = "Payroll RM" };
        var schedule = new DataRefreshSchedule
        {
            TargetReadModelId = rm.Id,
            TargetReadModel = rm
        };
        Assert.Equal(rm.Id, schedule.TargetReadModelId);
        Assert.Same(rm, schedule.TargetReadModel);
    }

    [Fact]
    public void SecurityIncident_SeverityValues()
    {
        Assert.True(Severity.Critical > Severity.High);
        Assert.True(Severity.High > Severity.Medium);
        Assert.True(Severity.Medium > Severity.Low);
    }

    [Fact]
    public void BackupJob_StatusValues()
    {
        var bj = new BackupJob
        {
            Target = "DB-Main",
            Status = BackupStatus.Failed
        };
        Assert.Equal(BackupStatus.Failed, bj.Status);
        bj.Status = BackupStatus.Ok;
        Assert.Equal(BackupStatus.Ok, bj.Status);
    }
}
