namespace Aegis.Core;

/// <summary>
/// Represents the current state of a workflow.
/// </summary>
public enum WorkflowStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    Paused
}

/// <summary>
/// Definition of a workflow as a DAG of tasks.
/// </summary>
public sealed class WorkflowDefinition
{
    public WorkflowId Id { get; }
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<TaskNode> Nodes { get; }
    public IReadOnlyDictionary<string, object> Metadata { get; }
    public DateTimeOffset CreatedAt { get; }

    private WorkflowDefinition(
        WorkflowId id,
        string name,
        string description,
        IReadOnlyList<TaskNode> nodes,
        IReadOnlyDictionary<string, object> metadata,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Description = description;
        Nodes = nodes;
        Metadata = metadata;
        CreatedAt = createdAt;
    }

    public static WorkflowDefinition Create(
        string name,
        string description,
        IEnumerable<TaskNode> nodes,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        var nodeList = nodes.ToList();
        ValidateDag(nodeList);
        return new WorkflowDefinition(
            WorkflowId.New(),
            name,
            description,
            nodeList,
            metadata ?? new Dictionary<string, object>(),
            DateTimeOffset.UtcNow);
    }

    private static void ValidateDag(IReadOnlyList<TaskNode> nodes)
    {
        var visited = new HashSet<TaskId>();
        var recursionStack = new HashSet<TaskId>();

        foreach (var node in nodes)
        {
            if (HasCycle(node, nodes, visited, recursionStack))
            {
                throw new InvalidOperationException($"Workflow contains a cycle involving task {node.Task.Id}");
            }
        }
    }

    private static bool HasCycle(TaskNode node, IReadOnlyList<TaskNode> allNodes, HashSet<TaskId> visited, HashSet<TaskId> recursionStack)
    {
        if (recursionStack.Contains(node.Task.Id))
            return true;

        if (visited.Contains(node.Task.Id))
            return false;

        visited.Add(node.Task.Id);
        recursionStack.Add(node.Task.Id);

        foreach (var depId in node.Task.Dependencies)
        {
            var depNode = allNodes.FirstOrDefault(n => n.Task.Id == depId);
            if (depNode != null && HasCycle(depNode, allNodes, visited, recursionStack))
                return true;
        }

        recursionStack.Remove(node.Task.Id);
        return false;
    }

    /// <summary>
    /// Gets all nodes that have no unmet dependencies (ready to execute).
    /// </summary>
    public IReadOnlyList<TaskNode> GetReadyNodes(IReadOnlySet<TaskId> completedTasks)
    {
        return Nodes
            .Where(n => !completedTasks.Contains(n.Task.Id))
            .Where(n => n.Task.Dependencies.All(d => completedTasks.Contains(d)))
            .ToList();
    }
}

/// <summary>
/// A node in the workflow DAG representing a task.
/// </summary>
public sealed record TaskNode
{
    public Task Task { get; init; }
    public IReadOnlyList<TaskId> DownstreamTasks { get; init; } = Array.Empty<TaskId>();
}