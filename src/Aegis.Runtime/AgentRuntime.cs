namespace Aegis.Runtime;

using Aegis.Core;
using System.Collections.Concurrent;

/// <summary>
/// Default implementation of the agent runtime.
/// </summary>
public sealed class AgentRuntime : IAgentRuntime, IDisposable
{
    private readonly ConcurrentDictionary<AgentId, Agent> _agents = new();
    private readonly ITaskScheduler _taskScheduler;
    private readonly IEventStore _eventStore;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger _logger;
    private bool _disposed;

    public AgentRuntime(
        ITaskScheduler taskScheduler,
        IEventStore eventStore,
        IToolRegistry toolRegistry,
        ILogger logger)
    {
        _taskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AgentId> StartAgentAsync(AgentConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var agentId = AgentId.New();
        var agent = new Agent(agentId, configuration, _taskScheduler, _eventStore, _toolRegistry, _logger);

        if (!_agents.TryAdd(agentId, agent))
        {
            throw new InvalidOperationException($"Failed to create agent {agentId}");
        }

        await agent.StartAsync(cancellationToken);

        await _eventStore.AppendAsync(new AgentCreatedEvent
        {
            AgentId = agentId,
            Name = configuration.Name,
            Configuration = configuration
        }, cancellationToken);

        _logger.LogInformation("Agent {AgentId} ({Name}) started", agentId, configuration.Name);
        return agentId;
    }

    public async Task StopAgentAsync(AgentId agentId, CancellationToken cancellationToken = default)
    {
        if (_agents.TryRemove(agentId, out var agent))
        {
            await agent.StopAsync(cancellationToken);

            await _eventStore.AppendAsync(new AgentStoppedEvent
            {
                AgentId = agentId
            }, cancellationToken);

            _logger.LogInformation("Agent {AgentId} stopped", agentId);
        }
    }

    public async Task<AgentStatus> GetAgentStatusAsync(AgentId agentId, CancellationToken cancellationToken = default)
    {
        if (_agents.TryGetValue(agentId, out var agent))
        {
            return await agent.GetStatusAsync(cancellationToken);
        }
        throw new KeyNotFoundException($"Agent {agentId} not found");
    }

    public async IAsyncEnumerable<AgentId> ListAgentsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var agentId in _agents.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return agentId;
        }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var agent in _agents.Values)
        {
            try { agent.Dispose(); } catch { }
        }
        _agents.Clear();
    }
}

/// <summary>
/// Event raised when an agent is created.
/// </summary>
public sealed record AgentCreatedEvent : Event
{
    public AgentId AgentId { get; init; }
    public string Name { get; init; } = string.Empty;
    public AgentConfiguration Configuration { get; init; } = new();
}

/// <summary>
/// Event raised when an agent is stopped.
/// </summary>
public sealed record AgentStoppedEvent : Event
{
    public AgentId AgentId { get; init; }
}