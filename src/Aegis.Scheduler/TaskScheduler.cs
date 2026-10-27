namespace Aegis.Scheduler;

using Aegis.Core;
using Aegis.Runtime;
using System.Collections.Concurrent;

/// <summary>
/// Task scheduler with multiple scheduling strategies.
/// </summary>
public interface ITaskScheduler : Aegis.Runtime.ITaskScheduler
{
    SchedulingStrategy Strategy { get; }
}

/// <summary>
/// Available scheduling strategies.
/// </summary>
public enum SchedulingStrategy
{
    Fifo,
    Priority,
    ShortestJobFirst,
    FairQueue,
    WeightedFairQueue,
    DeadlineAware
}

/// <summary>
/// Configuration for the task scheduler.
/// </summary>
public sealed record SchedulerConfiguration
{
    public SchedulingStrategy Strategy { get; init; } = SchedulingStrategy.Priority;
    public int WorkerCount { get; init; } = Environment.ProcessorCount;
    public TimeSpan QueuePollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
    public bool EnablePreemption { get; init; } = false;
    public TimeSpan PreemptionCheckInterval { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// A task queue entry with scheduling metadata.
/// </summary>
internal sealed record QueuedTask
{
    public Task Task { get; init; }
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;
    public int Priority => (int)Task.Priority;
    public TimeSpan EstimatedDuration => Task.Timeout;
    public DateTimeOffset? Deadline { get; init; }
    public int AttemptNumber { get; init; } = 1;
}