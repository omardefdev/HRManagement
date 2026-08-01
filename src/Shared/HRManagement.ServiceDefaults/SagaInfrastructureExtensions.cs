using System.Diagnostics.Metrics;
using HRManagement.SharedKernel.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

public static class SagaInfrastructureExtensions
{
    public static void ConfigureProcessInterventions(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessIntervention>(entity =>
        {
            entity.ToTable("ProcessInterventions");
            entity.HasIndex(e => new { e.TenantId, e.SagaType, e.CorrelationId, e.Status });
            entity.Property(e => e.SagaType).HasMaxLength(100);
            entity.Property(e => e.Reason).HasMaxLength(200);
            entity.Property(e => e.FailedParty).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.ResolutionNotes).HasMaxLength(500);
        });
    }

    public static void AddEntityFrameworkSagaRepository<TState, TDbContext>(
        this ISagaRegistrationConfigurator<TState> configurator)
        where TState : class, ISaga
        where TDbContext : DbContext
    {
        configurator.EntityFrameworkRepository(r =>
        {
            r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
            r.ExistingDbContext<TDbContext>();
            r.UsePostgres();
        });
    }
}

/// <summary>INT-003 saga metrics.</summary>
public sealed class SagaMetrics
{
    public static readonly string MeterName = "HRManagement.Saga";

    public SagaMetrics(IMeterFactory? meterFactory = null)
    {
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        Started = meter.CreateCounter<long>("saga_started");
        Completed = meter.CreateCounter<long>("saga_completed");
        TimedOut = meter.CreateCounter<long>("saga_timed_out");
        InterventionOpen = meter.CreateCounter<long>("saga_intervention_open");
        Compensated = meter.CreateCounter<long>("saga_compensated");
    }

    public Counter<long> Started { get; }
    public Counter<long> Completed { get; }
    public Counter<long> TimedOut { get; }
    public Counter<long> InterventionOpen { get; }
    public Counter<long> Compensated { get; }
}

public static class SagaOptionsDefaults
{
    /// <summary>Default party step timeout before intervention.</summary>
    public static readonly TimeSpan PartyTimeout = TimeSpan.FromMinutes(30);
}
