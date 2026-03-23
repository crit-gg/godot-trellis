namespace GodotTrellis;

/// <summary>
/// Resolves a dependency by walking up the scene tree from the owning node's
/// parent toward the root. Returns the first ancestor that implements
/// <see cref="IProvide{T}"/> or is itself T.
/// <para>
/// When the property type is <see cref="IEnumerable{T}"/>, collects all
/// matching ancestors up the chain instead of returning the first match.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [FromAncestor]
/// public partial ILogger Logger { get; }
///
/// [FromAncestor(Required = false)]
/// public partial IEventBus? EventBus { get; }
///
/// [FromAncestor]
/// public partial IEnumerable&lt;IBuffProvider&gt; ActiveBuffs { get; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FromAncestorAttribute : FromAttribute;