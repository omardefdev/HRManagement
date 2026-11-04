using System.Reflection;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRManagement.UnitTests.Messaging;

/// <summary>
/// PLAN-40: records, in one ordered log, every publish made through <see cref="Publish"/> ("publish:&lt;MessageType&gt;")
/// and every <c>SaveChanges</c> of a context built with <see cref="Interceptor"/> ("save"). With the bus outbox a publish
/// is written only by a later save of the same context, so a site is atomic when its publishes are followed by exactly one
/// save and no save comes between the state change and the publish.
/// </summary>
public sealed class OutboxOrderProbe
{
    public OutboxOrderProbe()
    {
        Interceptor = new SaveRecorder(Log);
        Publish = DispatchProxy.Create<IPublishEndpoint, RecordingPublishEndpoint>();
        ((RecordingPublishEndpoint)(object)Publish).Log = Log;
        ((RecordingPublishEndpoint)(object)Publish).Messages = Messages;
    }

    public List<string> Log { get; } = [];

    /// <summary>The first argument of each publish, in order (the message or its values).</summary>
    public List<object?> Messages { get; } = [];

    public IPublishEndpoint Publish { get; }

    public SaveChangesInterceptor Interceptor { get; }

    public DbContextOptions<TContext> InMemoryOptions<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(Interceptor)
            .Options;

    /// <summary>The log from the first publish on (what the action under test did after its reads and checks).</summary>
    public IReadOnlyList<string> FromFirstPublish()
    {
        var first = Log.FindIndex(e => e.StartsWith("publish:", StringComparison.Ordinal));
        return first < 0 ? [] : Log.Skip(first).ToList();
    }

    public void Clear() => Log.Clear();

    private sealed class SaveRecorder(List<string> log) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            log.Add("save");
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            log.Add("save");
            return ValueTask.FromResult(result);
        }
    }

    public class RecordingPublishEndpoint : DispatchProxy
    {
        public List<string> Log { get; set; } = [];
        public List<object?> Messages { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
                return null;
            if (targetMethod.Name == nameof(IPublishEndpoint.Publish))
            {
                var type = targetMethod.IsGenericMethod
                    ? targetMethod.GetGenericArguments()[0]
                    : args?.FirstOrDefault()?.GetType();
                Log.Add("publish:" + type?.Name);
                Messages.Add(args?.FirstOrDefault());
            }

            return targetMethod.ReturnType == typeof(Task) ? Task.CompletedTask : null;
        }
    }
}
