namespace GodotTrellis;

/// <summary>
/// Identifies the resolution strategy used to locate a dependency.
/// </summary>
public enum ResolveStrategy
{
    /// <summary>
    /// Walk up from <c>GetParent()</c> toward the root.
    /// Corresponds to <see cref="FromAncestorAttribute"/>.
    /// </summary>
    Ancestor,

    /// <summary>
    /// Check the node's <c>Owner</c> property, or walk up to the nearest
    /// node with a non-empty <c>SceneFilePath</c>.
    /// Corresponds to <see cref="FromOwnerAttribute"/>.
    /// </summary>
    Owner,

    /// <summary>
    /// Query <c>GetTree().GetNodesInGroup()</c> for a named group.
    /// Corresponds to <see cref="FromGroupAttribute"/>.
    /// </summary>
    Group,

    /// <summary>
    /// Search the node's direct children, or all descendants if deep.
    /// Corresponds to <see cref="FromChildAttribute"/>.
    /// </summary>
    Child
}