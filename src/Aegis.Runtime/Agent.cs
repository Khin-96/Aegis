namespace Aegis.Runtime;

using Aegis.Core;
using System.Collections.Concurrent;

/// <summary>
/// Represents an agent in the Aegis runtime.
/// </summary>
internal sealed class Agent : IDisposable
{
    private readonly AgentId _id;
    private readonly AgentConfiguration _configuration;
    private readonly ITaskScheduler _taskScheduler;
    private readonly IEventStore _eventStore;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<TaskId, TaskExecution> _executions = new();
    private readonly SemaphoreSlim _semaphore;
    private AgentState _state = AgentState.Starting;
    private DateTimeOffset _startedAt;
    private DateTimeOffset? _lastActivityAt;
    private int _completedTaskCount;
    private int _failedTaskCount;
    private bool _disposed;

    public Agent(
        AgentId id,
        AgentConfiguration configuration,
        ITaskScheduler taskScheduler,
        IEventStore eventStore,
        IToolRegistry toolRegistry,
        ILogger logger)
    {
        _id = id;
        _configuration = configuration;
        _taskScheduler = taskScheduler;
        _eventStore = eventStore;
        _toolRegistry = toolRegistry;
        _logger = logger;
        _semaphore = new SemaphoreSlim(configuration.ResourceLimits.MaxConcurrentTasks);
        _startedAt = DateTimeOffset.UtcNow;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _state = AgentState.Running;
        _logger.LogInformation("Agent {AgentId} started", _id);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _state = AgentState.Stopping;

        // Wait for active tasks to complete (with timeout)
        var waitTask = Task.WhenAll(_executions.Values.Select(e => e.CompletionTask));
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

        await Task.WhenAny(waitTask, timeoutTask);

        _state = AgentState.Stopped;
        _logger.LogInformation("Agent {AgentId} stopped", _id);
    }

    public async Task<AgentStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var activeCount = _executions.Count(e => e.Value.Status == TaskStatus.Running);

        return new AgentStatus
        {
            Id = _id,
            Name = _configuration.Name,
            State = _state,
            StartedAt = _startedAt,
            LastActivityAt = _lastActivityAt,
            ActiveTaskCount = activeCount,
            CompletedTaskCount = _completedTaskCount,
            FailedTaskCount = _failedTaskCount,
            ResourceUsage = new ResourceUsage
            {
                MemoryBytes = GC.GetTotalMemory(false),
                ThreadCount = ThreadPool.ThreadCount
            }
        };
    }

    public async Task<TaskResult> ExecuteTaskAsync(Task task, CancellationToken cancellationToken = default)
    {
        if (_state != AgentState.Running)
        {
            throw new InvalidOperationException($"Agent {_id} is not running (state: {_state})");
        }

        // Check security policy
        if (!_configuration.SecurityPolicy.IsToolAllowed(task.GetType().Name))
        {
            throw new UnauthorizedAccessException($"Tool {task.GetType().Name} not allowed by security policy");
        }

        await _semaphore.WaitAsync(cancellationToken);

        var execution = new TaskExecution(task);
        _executions.TryAdd(task.Id, execution);
        _state = AgentState.Busy;
        _lastActivityAt = DateTimeOffset.UtcNow;

        try
        {
            var result = await _taskScheduler.ScheduleAsync(task, _toolRegistry, _eventStore, _configuration.SecurityPolicy, cancellationToken);

            execution.Complete(result);

            if (result.Status == TaskStatus.Completed)
            {
                Interlocked.Increment(ref _completedTaskCount);
            }
            else
            {
                Interlocked.Increment(ref _failedTaskCount);
            }

            await _eventStore.AppendAsync(new TaskExecutedEvent
            {
                AgentId = _id,
                TaskId = task.Id,
                Result = result
            }, cancellationToken);

            return result;
        }
        finally
        {
            _executions.TryRemove(task.Id, out _);
            _semaphore.Release();

            if (_executions.IsEmpty)
            {
                _state = AgentState.Idle;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _semaphore.Dispose();
    }
}

/// <summary>
/// Tracks a task execution.
/// </summary>
internal sealed class TaskExecution
{
    public Task Task { get; }
    public TaskStatus Status { get; private set; } = TaskStatus.Pending;
    public TaskCompletionSource<TaskResult> CompletionSource { get; } = new();
    public Task CompletionTask => CompletionSource.Task;
    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.UtcNow;

    public TaskExecution(Task task)
    {
        Task = task;
    }

    public void Complete(TaskResult result)
    {
        Status = result.Status;
        CompletionSource.TrySetResult(result);
    }
}

/// <summary>
/// Event raised when a task is executed.
/// </summary>
public sealed record TaskExecutedEvent : Event
{
    public AgentId AgentId { get; init; }
    public TaskId TaskId { get; init; }
    public TaskResult Result { get; init; } = new();
}