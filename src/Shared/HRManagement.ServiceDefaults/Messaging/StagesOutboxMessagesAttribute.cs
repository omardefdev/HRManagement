namespace HRManagement.ServiceDefaults.Messaging;

/// <summary>
/// PLAN-40 (§1, §5 R1): marks a method that publishes through the bus outbox and deliberately does not save, so the
/// <c>OutboxMessage</c> rows it stages are committed by its caller's single <c>SaveChanges</c>. The static guard
/// (<c>OutboxPublishOrderingArchitectureTests</c>) treats a call to a marked method as a publish: the caller must save
/// after it, or be marked itself. Put it on the interface method and on the implementation.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class StagesOutboxMessagesAttribute : Attribute;
