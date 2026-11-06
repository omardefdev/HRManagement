namespace HRManagement.ServiceDefaults;

/// <summary>The person a background job acts for.</summary>
public sealed record ActingUser(Guid UserId, string? DisplayName);

/// <summary>
/// PLAN-43: carries the acting user where no HTTP request does, such as a retry job that replays a stored requester.
/// <see cref="ForwardActingUserHandler"/> uses it when the request has no signed-in user.
/// </summary>
public static class ActingUserScope
{
    private static readonly AsyncLocal<ActingUser?> Slot = new();

    public static ActingUser? Current => Slot.Value;

    /// <summary>Sets the acting user until the result is disposed, which restores the previous one.</summary>
    public static IDisposable Begin(Guid userId, string? displayName = null)
    {
        var previous = Slot.Value;
        Slot.Value = new ActingUser(userId, displayName);
        return new Restore(previous);
    }

    private sealed class Restore(ActingUser? previous) : IDisposable
    {
        public void Dispose() => Slot.Value = previous;
    }
}
