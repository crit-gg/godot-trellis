namespace GodotTrellis;

/// <summary>
/// Resolves a dependency by searching the owning node's children. Returns the
/// first child that implements <see cref="IProvide{T}"/> or is itself
/// T.
/// <para>
/// By default, only direct children are searched. Set <see cref="Deep"/> to
/// <c>true</c> for a recursive depth-first search of all descendants.
/// </para>
/// <para>
/// When the property type is <see cref="IEnumerable{T}"/>, collects all
/// matching children (or descendants if <see cref="Deep"/> is <c>true</c>).
/// </para>
/// <para>
/// <b>Cache behavior:</b> resolved values are cached on first access. If
/// child nodes are added or removed dynamically after initial resolution,
/// call <c>Deps.Invalidate()</c> to force resolution.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [FromChild]
/// public partial StatusComponent Status { get; }
///
/// [FromChild(Deep = true)]
/// public partial IEnumerable&lt;InteractableComponent&gt; Interactables { get; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FromChildAttribute : FromAttribute
{
    /// <summary>
    /// If <c>false</c> (default), only searches direct children.
    /// If <c>true</c>, performs a recursive depth-first search of all descendants.
    /// </summary>
    public bool Deep { get; set; }
}