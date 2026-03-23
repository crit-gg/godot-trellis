namespace GodotTrellis;

/// <summary>
/// Resolves a dependency by querying a named Godot group via
/// <c>GetTree().GetNodesInGroup()</c>. Returns the first node in the group
/// that implements <see cref="IProvide{T}"/> or is itself T.
/// <para>
/// The group name is required and must be passed as a constructor argument.
/// </para>
/// <para>
/// For single-type resolution, a warning is logged if multiple nodes in the
/// group match the requested type. When the property type is
/// <see cref="IEnumerable{T}"/>, all matching nodes are collected without warning.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [FromGroup("services")]
/// public partial IConfigProvider Config { get; }
///
/// [FromGroup("ui")]
/// public partial IEnumerable&lt;ITooltipProvider&gt; Tooltips { get; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FromGroupAttribute(string groupName) : FromAttribute
{
    /// <summary>
    /// The Godot group name to query. Nodes must be added to this group via
    /// <c>AddToGroup()</c> or the Godot editor.
    /// </summary>
    public string GroupName { get; } = groupName;
}