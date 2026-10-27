namespace Aegis.Core;

/// <summary>
/// Represents the current state of a task.
/// </summary>
public enum TaskStatus
{
    Pending,
    Scheduled,
    Running,
    Completed,
    Failed,
    Cancelled,
    Retrying
}

/// <summary>
/// Represents the priority of a task.
/// </summary>
public enum TaskPriority
{
    Low = 0,
    Normal = 50,
    High = 100,
    Critical = 200
}

/// <summary>
/// Base class for all tasks in the Aegis runtime.
/// </summary>
public abstract record Task
{
    public TaskId Id { get; init; } = TaskId.New();
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TaskPriority Priority { get; init; } = TaskPriority.Normal;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    public int MaxRetries { get; init; } = 3;
    public RetryPolicy RetryPolicy { get; init; } = RetryPolicy.ExponentialBackoff();
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
    public IReadOnlyList<TaskId> Dependencies { get; init; } = Array.Empty<TaskId>();
}

/// <summary>
/// Retry policy configuration.
/// </summary>
public record RetryPolicy
{
    public int MaxAttempts { get; init; }
    public TimeSpan BaseDelay { get; init; }
    public TimeSpan MaxDelay { get; init; }
    public double Multiplier { get; init; }
    public bool Jitter { get; init; }

    public static RetryPolicy ExponentialBackoff(
        int maxAttempts = 3,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        double multiplier = 2.0,
        bool jitter = true)
    {
        return new RetryPolicy
        {
            MaxAttempts = maxAttempts,
            BaseDelay = baseDelay ?? TimeSpan.FromSeconds(1),
            MaxDelay = maxDelay ?? TimeSpan.FromMinutes(1),
            Multiplier = multiplier,
            Jitter = jitter
        };
    }

    public static RetryPolicy FixedInterval(
        int maxAttempts = 3,
        TimeSpan? interval = null)
    {
        return new RetryPolicy
        {
            MaxAttempts = maxAttempts,
            BaseDelay = interval ?? TimeSpan.FromSeconds(5),
            MaxDelay = interval ?? TimeSpan.FromSeconds(5),
            Multiplier = 1.0,
            Jitter = false
        };
    }

    public static RetryPolicy NoRetry => new RetryPolicy
    {
        MaxAttempts = 1,
        BaseDelay = TimeSpan.Zero,
        MaxDelay = TimeSpan.Zero,
        Multiplier = 1.0,
        Jitter = false
    };
}

/// <summary>
/// Result of a task execution.
/// </summary>
public record TaskResult
{
    public TaskId TaskId { get; init; }
    public TaskStatus Status { get; init; }
    public object? Output { get; init; }
    public Exception? Error { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public int AttemptNumber { get; init; }
    public TimeSpan Duration => CompletedAt - StartedAt;

    public static TaskResult Success(TaskId taskId, object? output, DateTimeOffset startedAt, int attemptNumber = 1)
    {
        var completedAt = DateTimeOffset.UtcNow;
        return new TaskResult
        {
            TaskId = taskId,
            Status = TaskStatus.Completed,
            Output = output,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            AttemptNumber = attemptNumber
        };
    }

    public static TaskResult Failure(TaskId taskId, Exception error, DateTimeOffset startedAt, int attemptNumber = 1)
    {
        var completedAt = DateTimeOffset.UtcNow;
        return new TaskResult
        {
            TaskId = taskId,
            Status = TaskStatus.Failed,
            Error = error,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            AttemptNumber = attemptNumber
        };
    }
}