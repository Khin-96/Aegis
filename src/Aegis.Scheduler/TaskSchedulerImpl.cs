namespace Aegis.Scheduler;

using Aegis.Core;
using Aegis.Runtime;
using System.Collections.Concurrent;

/// <summary>
/// Default task scheduler implementation with multiple strategies.
/// </summary>
public sealed class TaskScheduler : ITaskScheduler
{
    private readonly SchedulerConfiguration _configuration;
    private readonly ConcurrentQueue<QueuedTask> _fifoQueue = new();
    private readonly ConcurrentPriorityQueue<QueuedTask> _priorityQueue = new();
    private readonly ConcurrentDictionary<TaskId, QueuedTask> _scheduledTasks = new();
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdownToken = new();
    private readonly List<Task> _workers = new();
    private SchedulingStrategy _strategy;

    public SchedulingStrategy Strategy => _strategy;

    public TaskScheduler(SchedulerConfiguration configuration, ILogger logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _strategy = configuration.Strategy;

        // Start worker tasks
        for (int i = 0; i < configuration.WorkerCount; i++)
        {
            var worker = Task.Run(() => WorkerLoopAsync(i, _shutdownToken.Token));
            _workers.Add(worker);
        }

        _logger.LogInformation("TaskScheduler started with {WorkerCount} workers using {Strategy} strategy",
            configuration.WorkerCount, _strategy);
    }

    public async Task<TaskResult> ScheduleAsync(
        Task task,
        IToolRegistry toolRegistry,
        IEventStore eventStore,
        SecurityPolicy securityPolicy,
        CancellationToken cancellationToken = default)
    {
        var queuedTask = new QueuedTask { Task = task };
        _scheduledTasks.TryAdd(task.Id, queuedTask);

        switch (_strategy)
        {
            case SchedulingStrategy.Fifo:
                _fifoQueue.Enqueue(queuedTask);
                break;
            case SchedulingStrategy.Priority:
            case SchedulingStrategy.ShortestJobFirst:
            case SchedulingStrategy.DeadlineAware:
                _priorityQueue.Enqueue(queuedTask, GetPriority(queuedTask));
                break;
            case SchedulingStrategy.FairQueue:
            case SchedulingStrategy.WeightedFairQueue:
                _priorityQueue.Enqueue(queuedTask, GetFairQueuePriority(queuedTask));
                break;
        }

        await eventStore.AppendAsync(new TaskScheduledEvent
        {
            TaskId = task.Id,
            Strategy = _strategy,
            Priority = task.Priority
        }, cancellationToken);

        _logger.LogDebug("Task {TaskId} ({TaskName}) scheduled with {Strategy} strategy",
            task.Id, task.Name, _strategy);

        // Wait for completion
        var result = await WaitForCompletionAsync(task.Id, cancellationToken);
        return result;
    }

    private int GetPriority(QueuedTask queuedTask)
    {
        return _strategy switch
        {
            SchedulingStrategy.Priority => -queuedTask.Priority, // Higher priority = lower value for min-heap
            SchedulingStrategy.ShortestJobFirst => (int)queuedTask.EstimatedDuration.TotalMilliseconds,
            SchedulingStrategy.DeadlineAware => queuedTask.Deadline.HasValue
                ? (int)(queuedTask.Deadline.Value - DateTimeOffset.UtcNow).TotalMilliseconds
                : int.MaxValue,
            _ => 0
        };
    }

    private int GetFairQueuePriority(QueuedTask queuedTask)
    {
        // Simple fair queue: prioritize by enqueue time, with weight consideration
        var waitTime = DateTimeOffset.UtcNow - queuedTask.EnqueuedAt;
        var basePriority = (int)waitTime.TotalMilliseconds;

        if (_strategy == SchedulingStrategy.WeightedFairQueue)
        {
            // Weight by priority (higher priority tasks get slightly better treatment)
            return basePriority - (queuedTask.Priority * 10);
        }

        return basePriority;
    }

    private async Task<TaskResult> WaitForCompletionAsync(TaskId taskId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_scheduledTasks.TryGetValue(taskId, out var queuedTask))
            {
                // Check if task has a result stored somewhere
                // In a real implementation, this would be more sophisticated
                await Task.Delay(50, cancellationToken);
            }
            else
            {
                // Task was removed from queue, should have completed
                break;
            }
        }

        // This is a simplified version - in reality, you'd have a proper completion mechanism
        return TaskResult.Success(taskId, null, DateTimeOffset.UtcNow);
    }

    private async Task WorkerLoopAsync(int workerId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Worker {WorkerId} started", workerId);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                QueuedTask? queuedTask = null;

                // Dequeue based on strategy
                switch (_strategy)
                {
                    case SchedulingStrategy.Fifo:
                        _fifoQueue.TryDequeue(out queuedTask);
                        break;
                    case SchedulingStrategy.Priority:
                    case SchedulingStrategy.ShortestJobFirst:
                    case SchedulingStrategy.DeadlineAware:
                    case SchedulingStrategy.FairQueue:
                    case SchedulingStrategy.WeightedFairQueue:
                        _priorityQueue.TryDequeue(out queuedTask, out _);
                        break;
                }

                if (queuedTask != null)
                {
                    _scheduledTasks.TryRemove(queuedTask.Task.Id, out _);
                    await ExecuteTaskAsync(queuedTask, cancellationToken);
                }
                else
                {
                    await Task.Delay(_configuration.QueuePollInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker {WorkerId} error", workerId);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        _logger.LogDebug("Worker {WorkerId} stopped", workerId);
    }

    private async Task ExecuteTaskAsync(QueuedTask queuedTask, CancellationToken cancellationToken)
    {
        var task = queuedTask.Task;
        var startedAt = DateTimeOffset.UtcNow;

        await eventStore.AppendAsync(new TaskStartedEvent
        {
            TaskId = task.Id,
            AttemptNumber = queuedTask.AttemptNumber
        }, cancellationToken);

        _logger.LogInformation("Executing task {TaskId} ({TaskName}), attempt {Attempt}",
            task.Id, task.Name, queuedTask.AttemptNumber);

        try
        {
            // Find appropriate executor
            var tool = toolRegistry.GetTool(task.GetType().Name);
            if (tool == null)
            {
                throw new InvalidOperationException($"No tool registered for task type {task.GetType().Name}");
            }

            // Check security policy
            if (!securityPolicy.IsToolAllowed(tool.Name))
            {
                throw new UnauthorizedAccessException($"Tool {tool.Name} not allowed by security policy");
            }

            // Execute with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(task.Timeout);

            var input = CreateToolInput(task);
            var result = await tool.ExecuteAsync(input, timeoutCts.Token);

            var taskResult = TaskResult.Success(task.Id, result, startedAt, queuedTask.AttemptNumber);

            await eventStore.AppendAsync(new TaskCompletedEvent
            {
                TaskId = task.Id,
                Result = taskResult
            }, cancellationToken);

            _logger.LogInformation("Task {TaskId} completed in {Duration}ms",
                task.Id, taskResult.Duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            var taskResult = TaskResult.Failure(task.Id, ex, startedAt, queuedTask.AttemptNumber);

            await eventStore.AppendAsync(new TaskFailedEvent
            {
                TaskId = task.Id,
                Error = ex.Message,
                AttemptNumber = queuedTask.AttemptNumber,
                WillRetry = queuedTask.AttemptNumber < task.MaxRetries
            }, cancellationToken);

            _logger.LogWarning(ex, "Task {TaskId} failed (attempt {Attempt}/{MaxRetries})",
                task.Id, queuedTask.AttemptNumber, task.MaxRetries);

            // Handle retry
            if (queuedTask.AttemptNumber < task.MaxRetries)
            {
                var delay = CalculateRetryDelay(task.RetryPolicy, queuedTask.AttemptNumber);
                await Task.Delay(delay, cancellationToken);

                var retryTask = new QueuedTask
                {
                    Task = task,
                    AttemptNumber = queuedTask.AttemptNumber + 1,
                    EnqueuedAt = DateTimeOffset.UtcNow
                };

                _scheduledTasks.TryAdd(task.Id, retryTask);

                switch (_strategy)
                {
                    case SchedulingStrategy.Fifo:
                        _fifoQueue.Enqueue(retryTask);
                        break;
                    default:
                        _priorityQueue.Enqueue(retryTask, GetPriority(retryTask));
                        break;
                }
            }
        }
    }

    private object CreateToolInput(Task task)
    {
        // In a real implementation, this would serialize/deserialize based on tool input type
        return task;
    }

    private TimeSpan CalculateRetryDelay(RetryPolicy policy, int attemptNumber)
    {
        var delay = TimeSpan.FromTicks((long)(policy.BaseDelay.Ticks * Math.Pow(policy.Multiplier, attemptNumber - 1)));

        if (delay > policy.MaxDelay)
            delay = policy.MaxDelay;

        if (policy.Jitter)
        {
            var jitter = Random.Shared.NextDouble() * 0.3 * delay.TotalMilliseconds;
            delay = delay.Add(TimeSpan.FromMilliseconds(jitter));
        }

        return delay;
    }

    public void Dispose()
    {
        _shutdownToken.Cancel();
        Task.WaitAll(_workers.ToArray(), TimeSpan.FromSeconds(10));
        _shutdownToken.Dispose();
    }
}

/// <summary>
/// Thread-safe priority queue implementation.
/// </summary>
internal sealed class ConcurrentPriorityQueue<T>
{
    private readonly PriorityQueue<T, int> _queue = new();
    private readonly object _lock = new();

    public void Enqueue(T item, int priority)
    {
        lock (_lock)
        {
            _queue.Enqueue(item, priority);
        }
    }

    public bool TryDequeue(out T item, out int priority)
    {
        lock (_lock)
        {
            return _queue.TryDequeue(out item, out priority);
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }
}

// Events for task scheduling
public sealed record TaskScheduledEvent : Event
{
    public TaskId TaskId { get; init; }
    public SchedulingStrategy Strategy { get; init; }
    public TaskPriority Priority { get; init; }
}

public sealed record TaskStartedEvent : Event
{
    public TaskId TaskId { get; init; }
    public int AttemptNumber { get; init; }
}

public sealed record TaskCompletedEvent : Event
{
    public TaskId TaskId { get; init; }
    public TaskResult Result { get; init; } = new();
}

public sealed record TaskFailedEvent : Event
{
    public TaskId TaskId { get; init; }
    public string Error { get; init; } = string.Empty;
    public int AttemptNumber { get; init; }
    public bool WillRetry { get; init; }
}