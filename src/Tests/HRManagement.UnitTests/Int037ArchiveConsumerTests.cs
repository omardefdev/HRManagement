using System.Diagnostics.Metrics;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.TestSupport.CodeGeneration;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Records.Archive.Infrastructure.Consumers;
using Records.Archive.Infrastructure.Data;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-37 (§5, G-COD-06): the seven archive consumer sites never acknowledge a message without an archive item. When
/// HR.Core issues no number they throw <see cref="CodeGenerationUnavailableException"/> (retried by MassTransit, then
/// dead-lettered), add no row and count the failure.
/// </summary>
public class Int037ArchiveConsumerTests
{
    public static TheoryData<Type, Type> Consumers() => new()
    {
        { typeof(LeaveApprovedConsumer), typeof(ILeaveApproved) },
        { typeof(AdvanceApprovedConsumer), typeof(IAdvanceApproved) },
        { typeof(SeparationCompletedConsumer), typeof(ISeparationCompleted) },
        { typeof(MedicalRecommendationIssuedArchiveConsumer), typeof(IMedicalRecommendationIssued) },
        { typeof(MedicalDecisionIssuedArchiveConsumer), typeof(IMedicalDecisionIssued) },
        { typeof(HireCaseCompletedConsumer), typeof(IHireCaseCompleted) },
        { typeof(ComplaintResolvedArchiveConsumer), typeof(IComplaintResolved) }
    };

    [Theory]
    [MemberData(nameof(Consumers))]
    public async Task Missing_reference_throws_adds_no_row_and_counts_the_failure(Type consumerType, Type messageType)
    {
        var codes = new FakeCodeGenerationClient { GenerateHandler = _ => new CodeGenerationResult { Code = "" } };
        using var provider = BuildServices(codes);
        var db = provider.GetRequiredService<ArchiveDbContext>();
        var failures = new List<string>();
        using var listener = ListenForFailures(failures);

        var ex = await Assert.ThrowsAsync<CodeGenerationUnavailableException>(() => Consume(provider, consumerType, messageType));

        Assert.Equal(CodeEntityKind.ArchiveReference, ex.EntityKind);
        Assert.Equal(0, await db.ArchiveItems.CountAsync());
        Assert.Contains(messageType.Name, failures);
        Assert.Single(codes.Requests);
    }

    [Theory]
    [MemberData(nameof(Consumers))]
    public async Task Issued_reference_creates_exactly_one_item_even_when_redelivered(Type consumerType, Type messageType)
    {
        var codes = new FakeCodeGenerationClient();
        using var provider = BuildServices(codes);
        var db = provider.GetRequiredService<ArchiveDbContext>();
        var message = CreateMessage(messageType);

        await Consume(provider, consumerType, messageType, message);
        await Consume(provider, consumerType, messageType, message);

        var item = Assert.Single(await db.ArchiveItems.ToListAsync());
        Assert.False(string.IsNullOrWhiteSpace(item.ReferenceNumber));
    }

    [Theory]
    [InlineData(typeof(MedicalRecommendationIssuedArchiveConsumer), typeof(IMedicalRecommendationIssued))]
    [InlineData(typeof(MedicalDecisionIssuedArchiveConsumer), typeof(IMedicalDecisionIssued))]
    public async Task Newer_schema_version_is_dead_lettered_not_acknowledged(Type consumerType, Type messageType)
    {
        var codes = new FakeCodeGenerationClient();
        using var provider = BuildServices(codes);
        var db = provider.GetRequiredService<ArchiveDbContext>();
        var message = CreateMessage(messageType, ("SchemaVersion", 99));

        var ex = await Assert.ThrowsAsync<UnsupportedEventSchemaException>(() => Consume(provider, consumerType, messageType, message));

        // InvalidOperationException is on the bus ignore list: no retries, straight to _error.
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.Equal(0, await db.ArchiveItems.CountAsync());
        Assert.Empty(codes.Requests);
    }

    [Fact]
    public void Unavailable_exception_is_retried_not_ignored_by_the_bus_policy()
    {
        // MassTransitExtensions ignores InvalidOperationException / ArgumentException (they go straight to _error).
        Assert.Equal(typeof(Exception), typeof(CodeGenerationUnavailableException).BaseType);
    }

    private static ServiceProvider BuildServices(ICodeGenerationClient codes)
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ArchiveDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton(codes);
        return services.BuildServiceProvider();
    }

    private static object CreateMessage(Type messageType, params (string Name, object Value)[] overrides)
    {
        var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(messageType))!;
        mock.DefaultValueProvider = DefaultValueProvider.Mock;
        foreach (var property in messageType.GetProperties().Concat(messageType.GetInterfaces().SelectMany(i => i.GetProperties())))
        {
            if (property.PropertyType == typeof(Guid))
                SetupProperty(mock, messageType, property.Name, Guid.NewGuid());
            else if (property.PropertyType == typeof(string))
                SetupProperty(mock, messageType, property.Name, property.Name + "-value");
        }

        foreach (var (name, value) in overrides)
            SetupProperty(mock, messageType, name, value);

        return mock.Object;
    }

    private static void SetupProperty(Mock mock, Type messageType, string name, object value)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(messageType, "m");
        var body = System.Linq.Expressions.Expression.Property(parameter, name);
        var lambda = System.Linq.Expressions.Expression.Lambda(body, parameter);
        var setupGet = mock.GetType().GetMethods()
            .Single(m => m.Name == "SetupGet" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .MakeGenericMethod(body.Type);
        var setup = setupGet.Invoke(mock, [lambda])!;
        setup.GetType().GetMethod("Returns", [body.Type])!.Invoke(setup, [value]);
    }

    private static Task Consume(IServiceProvider provider, Type consumerType, Type messageType, object? message = null) =>
        (Task)typeof(Int037ArchiveConsumerTests)
            .GetMethod(nameof(ConsumeTyped), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(messageType)
            .Invoke(null, [provider, consumerType, message ?? CreateMessage(messageType)])!;

    private static async Task ConsumeTyped<TMessage>(IServiceProvider provider, Type consumerType, object message)
        where TMessage : class
    {
        using var scope = provider.CreateScope();
        var consumer = (IConsumer<TMessage>)ActivatorUtilities.CreateInstance(scope.ServiceProvider, consumerType);
        var context = new Mock<ConsumeContext<TMessage>> { DefaultValue = DefaultValue.Mock };
        context.SetupGet(c => c.Message).Returns((TMessage)message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        context.SetupGet(c => c.MessageId).Returns(NewId.NextGuid());
        await consumer.Consume(context.Object);
    }

    private static MeterListener ListenForFailures(List<string> eventTypes)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ArchiveReferenceMetrics.MeterName && instrument.Name == ArchiveReferenceMetrics.FailuresCounterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "event_type")
                    lock (eventTypes) eventTypes.Add((string)tag.Value!);
            }
        });
        listener.Start();
        return listener;
    }
}
