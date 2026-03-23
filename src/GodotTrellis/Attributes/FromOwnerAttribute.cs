namespace GodotTrellis;

/// <summary>
/// Resolves a dependency from the owning node's scene root.
/// <para>
/// By default, checks the node's <c>Owner</c> property. If
/// <see cref="UseSceneFilePath"/> is <c>true</c>, walks up the tree and
/// returns the first ancestor with a non-empty <c>SceneFilePath</c>
/// (i.e., the root of an instanced <c>PackedScene</c>).
/// </para>
/// <para>
/// The matched node must implement <see cref="IProvide{T}"/> or be
/// itself T.
/// </para>
/// <para>
/// When <see cref="UseSceneFilePath"/> is <c>true</c> and the property type
/// is <see cref="IEnumerable{T}"/>, collects all matching scene roots up the
/// chain.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [FromOwner]
/// public partial SceneController SceneRoot { get; }
///
/// [FromOwner(UseSceneFilePath = true)]
/// public partial IResourceLoader Loader { get; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FromOwnerAttribute : FromAttribute
{
    /// <summary>
    /// If <c>false</c> (default), resolves from the node's <c>Owner</c> property directly.
    /// If <c>true</c>, walks up the tree to the nearest ancestor with a non-empty
    /// <c>SceneFilePath</c>, identifying the root of an instanced <c>PackedScene</c>.
    /// </summary>
    public bool UseSceneFilePath { get; set; }
}