namespace Aegis.Core;

/// <summary>
/// Unique identifier for a task in the Aegis runtime.
/// </summary>
public readonly record struct TaskId(Guid Value)
{
    public static TaskId New() => new(Guid.NewGuid());
    public static TaskId Parse(string s) => new(Guid.Parse(s));
    public static bool TryParse(string s, out TaskId id)
    {
        if (Guid.TryParse(s, out var guid))
        {
            id = new TaskId(guid);
            return true;
        }
        id = default;
        return false;
    }

    public override string ToString() => Value.ToString("N");
    public static implicit operator Guid(TaskId id) => id.Value;
    public static implicit operator TaskId(Guid guid) => new(guid);
}

/// <summary>
/// Unique identifier for a workflow in the Aegis runtime.
/// </summary>
public readonly record struct WorkflowId(Guid Value)
{
    public static WorkflowId New() => new(Guid.NewGuid());
    public static WorkflowId Parse(string s) => new(Guid.Parse(s));
    public static bool TryParse(string s, out WorkflowId id)
    {
        if (Guid.TryParse(s, out var guid))
        {
            id = new WorkflowId(guid);
            return true;
        }
        id = default;
        return false;
    }

    public override string ToString() => Value.ToString("N");
    public static implicit operator Guid(WorkflowId id) => id.Value;
    public static implicit operator WorkflowId(Guid guid) => new(guid);
}

/// <summary>
/// Unique identifier for a message in the Aegis runtime.
/// </summary>
public readonly record struct MessageId(Guid Value)
{
    public static MessageId New() => new(Guid.NewGuid());
    public static MessageId Parse(string s) => new(Guid.Parse(s));
    public static bool TryParse(string s, out MessageId id)
    {
        if (Guid.TryParse(s, out var guid))
        {
            id = new MessageId(guid);
            return true;
        }
        id = default;
        return false;
    }

    public override string ToString() => Value.ToString("N");
    public static implicit operator Guid(MessageId id) => id.Value;
    public static implicit operator MessageId(Guid guid) => new(guid);
}