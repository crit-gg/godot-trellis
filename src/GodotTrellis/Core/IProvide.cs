namespace GodotTrellis;

/// <summary>
/// Marks a node as a provider of <typeparamref name="T"/>. Nodes implementing
/// this interface can be discovered by the resolution system and supply values
/// to dependent nodes via <see cref="GetProvidedValue"/>.
/// <para>
/// A single node may implement <see cref="IProvide{T}"/> for multiple types.
/// </para>
/// <para>
/// If the provided value can change during the node's lifetime, fire the
/// <see cref="ProvidedValueChanged"/> event to notify dependents. The resolver will
/// pull <see cref="GetProvidedValue"/> without performing a tree walk.
/// </para>
/// </summary>
/// <typeparam name="T">
/// The type of value this node provides. Must be a reference type.
/// </typeparam>
public interface IProvide<out T> where T : class
{
    /// <summary>
    /// Returns the current value of the provided dependency.
    /// Must not return <c>null</c> — if the value is not yet available,
    /// the node should not implement <see cref="IProvide{T}"/> until it is.
    /// </summary>
    T GetProvidedValue();

    /// <summary>
    /// Raised when the provided value has changed. Dependents subscribed
    /// through the resolver will re pull <see cref="GetProvidedValue"/> automatically.
    /// <para>
    /// Providers whose value never changes do not need to implement this
    /// event. The default implementation is a no-op.
    /// </para>
    /// </summary>
    event Action? ProvidedValueChanged;
}