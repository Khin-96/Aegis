namespace Aegis.Core;

/// <summary>
/// Unique identifier for an agent in the Aegis runtime.
/// </summary>
public readonly record struct AgentId(Guid Value)
{
    public static AgentId New() => new(Guid.NewGuid());
    public static AgentId Parse(string s) => new(Guid.Parse(s));
    public static bool TryParse(string s, out AgentId id)
    {
        if (Guid.TryParse(s, out var guid))
        {
            id = new AgentId(guid);
            return true;
        }
        id = default;
        return false;
    }

    public override string ToString() => Value.ToString("N");
    public static implicit operator Guid(AgentId id) => id.Value;
    public static implicit operator AgentId(Guid guid) => new(guid);
}