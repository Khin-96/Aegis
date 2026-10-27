namespace Aegis.Runtime;

using Aegis.Core;

/// <summary>
/// Interface for executing tasks.
/// </summary>
public interface ITaskExecutor
{
    Task<TaskResult> ExecuteAsync(Task task, CancellationToken cancellationToken = default);
    bool CanExecute(Task task);
}

/// <summary>
/// Interface for the agent runtime.
/// </summary>
public interface IAgentRuntime
{
    Task<AgentId> StartAgentAsync(AgentConfiguration configuration, CancellationToken cancellationToken = default);
    Task StopAgentAsync(AgentId agentId, CancellationToken cancellationToken = default);
    Task<AgentStatus> GetAgentStatusAsync(AgentId agentId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<AgentId> ListAgentsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for an agent.
/// </summary>
public sealed record AgentConfiguration
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<Type> TaskTypes { get; init; } = Array.Empty<Type>();
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();
    public ResourceLimits ResourceLimits { get; init; } = ResourceLimits.Default;
    public SecurityPolicy SecurityPolicy { get; init; } = SecurityPolicy.Default;
}

/// <summary>
/// Resource limits for an agent.
/// </summary>
public sealed record ResourceLimits
{
    public long MaxMemoryBytes { get; init; } = 512 * 1024 * 1024; // 512MB
    public TimeSpan MaxCpuTime { get; init; } = TimeSpan.FromMinutes(10);
    public int MaxConcurrentTasks { get; init; } = 10;
    public long MaxDiskBytes { get; init; } = 1024 * 1024 * 1024; // 1GB

    public static ResourceLimits Default => new();
    public static ResourceLimits Unlimited => new()
    {
        MaxMemoryBytes = long.MaxValue,
        MaxCpuTime = TimeSpan.MaxValue,
        MaxConcurrentTasks = int.MaxValue,
        MaxDiskBytes = long.MaxValue
    };
}

/// <summary>
/// Security policy for an agent.
/// </summary>
public sealed record SecurityPolicy
{
    public IReadOnlySet<string> AllowedTools { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> DeniedTools { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> AllowedDomains { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> DeniedDomains { get; init; } = new HashSet<string>();
    public bool AllowShellExecution { get; init; } = false;
    public bool AllowFileSystemWrite { get; init; } = false;
    public bool AllowNetworkAccess { get; init; } = true;

    public static SecurityPolicy Default => new()
    {
        AllowedTools = new HashSet<string> { "http.get", "http.post", "search", "read" },
        DeniedTools = new HashSet<string> { "shell.exec", "file.delete", "db.write" },
        AllowNetworkAccess = true
    };

    public static SecurityPolicy Restricted => new()
    {
        AllowedTools = new HashSet<string> { "read" },
        DeniedTools = new HashSet<string> { "*" },
        AllowNetworkAccess = false,
        AllowFileSystemWrite = false,
        AllowShellExecution = false
    };

    public static SecurityPolicy Elevated => new()
    {
        AllowedTools = new HashSet<string> { "*" },
        DeniedTools = new HashSet<string>(),
        AllowNetworkAccess = true,
        AllowFileSystemWrite = true,
        AllowShellExecution = true
    };

    public bool IsToolAllowed(string toolName)
    {
        if (DeniedTools.Contains("*") || DeniedTools.Contains(toolName))
            return false;
        if (AllowedTools.Contains("*") || AllowedTools.Contains(toolName))
            return true;
        return false;
    }
}

/// <summary>
/// Status of an agent.
/// </summary>
public sealed record AgentStatus
{
    public AgentId Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public AgentState State { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }
    public int ActiveTaskCount { get; init; }
    public int CompletedTaskCount { get; init; }
    public int FailedTaskCount { get; init; }
    public ResourceUsage ResourceUsage { get; init; } = new();
}

/// <summary>
/// Current state of an agent.
/// </summary>
public enum AgentState
{
    Starting,
    Running,
    Idle,
    Busy,
    Stopping,
    Stopped,
    Failed
}

/// <summary>
/// Resource usage of an agent.
/// </summary>
public sealed record ResourceUsage
{
    public long MemoryBytes { get; init; }
    public TimeSpan CpuTime { get; init; }
    public int ThreadCount { get; init; }
    public long DiskBytes { get; init; }
}