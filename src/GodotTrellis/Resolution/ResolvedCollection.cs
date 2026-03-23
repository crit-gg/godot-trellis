using Godot;

namespace GodotTrellis;

/// <summary>
/// Caches a collection of resolved dependencies for <see cref="IEnumerable{T}"/>
/// properties. Tracks each value alongside its provider node for revalidation.
/// Subscribes to each provider's <see cref="IProvide{T}.Changed"/> event.
/// </summary>
/// <typeparam name="T">The dependency type.</typeparam>
public sealed class ResolvedCollection<T> : IResolvedDependency where T : class
{
    private readonly Func<Node, List<(T value, Node provider)>> _resolveFunc;
    private readonly Action<Type>? _onChanged;

    private List<(T value, Node provider)> _entries;

    /// <summary>
    /// The currently resolved values.
    /// </summary>
    public IReadOnlyList<T> Values => _entries.Select(e => e.value).ToList();

    /// <summary>
    /// The provider nodes that supplied the current values, in the same order
    /// as <see cref="Values"/>.
    /// </summary>
    public IReadOnlyList<Node> Providers => _entries.Select(e => e.provider).ToList();

    /// <inheritdoc />
    public Type DependencyType => typeof(T);

    /// <summary>
    /// Creates a new resolved collection entry.
    /// </summary>
    /// <param name="entries">The resolved value/provider pairs.</param>
    /// <param name="resolveFunc">
    /// The resolution function to run during revalidation. Receives the
    /// owner node and returns all matching value/provider pairs.
    /// </param>
    /// <param name="onChanged">
    /// Optional callback invoked when any provider's value changes or when
    /// revalidation detects the set of providers has changed.
    /// </param>
    internal ResolvedCollection(
        List<(T value, Node provider)> entries,
        Func<Node, List<(T value, Node provider)>> resolveFunc,
        Action<Type>? onChanged)
    {
        _entries = entries;
        _resolveFunc = resolveFunc;
        _onChanged = onChanged;

        SubscribeToProvider();
    }

    /// <inheritdoc />
    public bool Revalidate(Node owner)
    {
        UnsubscribeFromProvider();

        var newEntries = _resolveFunc(owner);

        var unchanged = newEntries.Count == _entries.Count
            && newEntries.Zip(_entries).All(pair =>
                ReferenceEquals(pair.First.provider, pair.Second.provider));

        _entries = newEntries;
        SubscribeToProvider();

        return unchanged;
    }

    /// <inheritdoc />
    public void SubscribeToProvider()
    {
        foreach (var (_, provider) in _entries)
        {
            if (provider is IProvide<T> provide)
                provide.Changed += OnProviderChanged;
        }
    }

    /// <inheritdoc />
    public void UnsubscribeFromProvider()
    {
        foreach (var (_, provider) in _entries)
        {
            if (provider is IProvide<T> provide)
                provide.Changed -= OnProviderChanged;
        }
    }

    private void OnProviderChanged()
    {
        // Pull all values from their providers. We don't know which
        // specific provider fired, so refresh the full list.
        for (var i = 0; i < _entries.Count; i++)
        {
            var (_, provider) = _entries[i];
            if (provider is IProvide<T> provide)
                _entries[i] = (provide.Value(), provider);
        }

        _onChanged?.Invoke(DependencyType);
    }
}