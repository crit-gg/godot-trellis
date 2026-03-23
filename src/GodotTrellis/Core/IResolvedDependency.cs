using Godot;

namespace GodotTrellis;

/// <summary>
/// Allows the <see cref="DependencyResolver"/> to iterate and revalidate cached entries
/// without knowing their concrete type parameter.
/// </summary>
public interface IResolvedDependency
{
    /// <summary>
    /// The type of dependency this entry resolves.
    /// </summary>
    Type DependencyType { get; }

    /// <summary>
    /// Runs resolution for this dependency against the current scene tree.
    /// Compares the new provider to the previously cached provider.
    /// </summary>
    /// <param name="owner">The node that owns this dependency.</param>
    /// <returns>
    /// <c>true</c> if the provider is unchanged (same node instance),
    /// <c>false</c> if the provider changed or the dependency resolved
    /// to a different node.
    /// </returns>
    bool Revalidate(Node owner);

    /// <summary>
    /// Subscribes to the current provider's <see cref="IProvide{T}.Changed"/>
    /// event so that value changes are detected without a tree walk.
    /// </summary>
    void SubscribeToProvider();

    /// <summary>
    /// Unsubscribes from the current provider's <see cref="IProvide{T}.Changed"/>
    /// event. Called before revalidation or cache invalidation to prevent
    /// stale subscriptions.
    /// </summary>
    void UnsubscribeFromProvider();
}