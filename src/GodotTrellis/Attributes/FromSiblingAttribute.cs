namespace GodotTrellis;

/// <summary>
/// Resolves a dependency by searching the owning node's siblings (nodes that
/// share the same parent). The owning node itself is excluded from matching.
/// Returns the first sibling that implements <see cref="IProvide{T}"/> or is
/// itself T.
/// <para>
/// When the property type is <see cref="IEnumerable{T}"/>, collects all
/// matching siblings.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [FromSibling]
/// public partial IAudioService Audio { get; }
///
/// [FromSibling]
/// public partial IEnumerable&lt;ITooltipProvider&gt; Tooltips { get; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FromSiblingAttribute : FromAttribute;
