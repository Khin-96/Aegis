namespace Aegis.Core;

/// <summary>
/// Base event for event sourcing in Aegis.
/// </summary>
public abstract record Event
{
    public EventId Id { get; init; } = EventId.New();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CausationId? CausationId { get; init; }
    public CorrelationId? CorrelationId { get; init; }
}

/// <summary>
/// Unique identifier for an event.
/// </summary>
public readonly record struct EventId(Guid Value)
{
    public static EventId New() => new(Guid.NewGuid());
    public static EventId Parse(string s) => new(Guid.Parse(s));
    public override string ToString() => Value.ToString("N");
    public static implicit operator Guid(EventId id) => id.Value;
    public static implicit operator EventId(Guid guid) => new(guid);
}

/// <summary>
/// Causation ID linking events in a causal chain.
/// </summary>
public readonly record struct CausationId(Guid Value)
{
    public static CausationId New() => new(Guid.NewGuid());
    public static CausationId Parse(string s) => new(Guid.Parse(s));
    public override string ToString() => Value.ToString("N");
    public static implicit operator Guid(CausationId id) => id.Value;
    public static implicit operator CausationId(Guid guid) => new(guid);
}

/// <summary>
/// Correlation ID linking related events across a workflow.
/// </summary>
public readonly record struct CorrelationId(Guid Value)
{
    public static CorrelationId New() => new(Guid.NewGuid());
    public static CorrelationId Parse(string s) => new(Guid.Parse(s));
    public override string ToString() => Value.ToString("N");
    public static implicit operator Guid(CorrelationId id) => id.Value;
    public static implicit operator CorrelationId(Guid guid) => new(guid);
}