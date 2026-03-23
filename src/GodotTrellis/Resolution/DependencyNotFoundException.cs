namespace GodotTrellis;

/// <summary>
/// Thrown when a required dependency cannot be resolved.
/// </summary>
public sealed class DependencyNotFoundException : InvalidOperationException
{
    /// <summary>
    /// The type of the node that attempted to resolve the dependency.
    /// </summary>
    public Type OwnerType { get; }

    /// <summary>
    /// The dependency type that could not be found.
    /// </summary>
    public Type DependencyType { get; }

    /// <summary>
    /// The resolution strategy that was used.
    /// </summary>
    public ResolveStrategy Strategy { get; }

    public DependencyNotFoundException(
        Type ownerType,
        Type dependencyType,
        ResolveStrategy strategy)
        : base($"{ownerType.Name} could not resolve {dependencyType.Name} " +
               $"using strategy {strategy}. No matching provider found in the scene tree.")
    {
        OwnerType = ownerType;
        DependencyType = dependencyType;
        Strategy = strategy;
    }
}