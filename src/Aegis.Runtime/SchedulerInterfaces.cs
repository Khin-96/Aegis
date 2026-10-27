namespace Aegis.Runtime;

using Aegis.Core;

/// <summary>
/// Interface for task scheduling.
/// </summary>
public interface ITaskScheduler
{
    Task<TaskResult> ScheduleAsync(
        Task task,
        IToolRegistry toolRegistry,
        IEventStore eventStore,
        SecurityPolicy securityPolicy,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for tool registry.
/// </summary>
public interface IToolRegistry
{
    ITool? GetTool(string name);
    IReadOnlyList<ITool> GetAllTools();
    void RegisterTool(ITool tool);
    bool UnregisterTool(string name);
}

/// <summary>
/// Interface for a tool.
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    Type InputType { get; }
    Type OutputType { get; }
    Task<object> ExecuteAsync(object input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for event store.
/// </summary>
public interface IEventStore
{
    Task AppendAsync(Event @event, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Event>> GetEventsAsync(
        CorrelationId correlationId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Event>> GetEventsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Event>> GetEventsForAgentAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Simple logging interface.
/// </summary>
public interface ILogger
{
    void LogInformation(string message, params object[] args);
    void LogWarning(string message, params object[] args);
    void LogError(string message, params object[] args);
    void LogError(Exception exception, string message, params object[] args);
    void LogDebug(string message, params object[] args);
}