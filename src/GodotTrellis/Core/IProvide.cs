namespace GodotTrellis;

/// <summary>
/// Marks a node as a provider of <typeparamref name="T"/>. Nodes implementing
/// this interface can be discovered by the resolution system and supply values
/// to dependent nodes via <see cref="Value"/>.
/// <para>
/// A single node may implement <see cref="IProvide{T}"/> for multiple types.
/// </para>
/// <para>
/// If the provided value can change during the node's lifetime, fire the
/// <see cref="Changed"/> event to notify dependents. The resolver will
/// pull <see cref="Value"/> without performing a tree walk.
/// </para>
/// </summary>
/// <typeparam name="T">
/// The type of value this node provides. Must be a reference type.
/// </typeparam>
/// <example>
/// <code>
/// // Simple static provider — value never changes, no need to fire Changed.
/// public partial class AppRoot : Node, IProvide&lt;ILogger&gt;
/// {
///     private readonly ILogger _logger = new ConsoleLogger();
///     public ILogger Value() => _logger;
/// }
///
/// // Dynamic provider — value can change at runtime.
/// public partial class ThemeManager : Node, IProvide&lt;ITheme&gt;
/// {
///     private ITheme _current = new DefaultTheme();
///
///     public ITheme Value() => _current;
///
///     public void SetTheme(ITheme theme)
///     {
///         _current = theme;
///         Changed?.Invoke();
///     }
/// }
/// </code>
/// </example>
public interface IProvide<out T> where T : class
{
    /// <summary>
    /// Returns the current value of the provided dependency.
    /// Must not return <c>null</c> — if the value is not yet available,
    /// the node should not implement <see cref="IProvide{T}"/> until it is.
    /// </summary>
    T Value();

    /// <summary>
    /// Raised when the provided value has changed. Dependents subscribed
    /// through the resolver will re pull <see cref="Value"/> automatically.
    /// <para>
    /// Providers whose value never changes do not need to implement this
    /// event. The default implementation is a no-op.
    /// </para>
    /// </summary>
    event Action? Changed
    {
        add { }
        remove { }
    }
}