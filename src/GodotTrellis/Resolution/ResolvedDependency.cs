using Godot;

namespace GodotTrellis;

/// <summary>
/// Caches a single resolved dependency, tracking both the value and the
/// provider node that supplied it. Handles revalidation after reparenting
/// and subscribes to the provider's <see cref="IProvide{T}.ProvidedValueChanged"/> event
/// for automatic value refresh.
/// </summary>
/// <typeparam name="T">The dependency type.</typeparam>
public sealed class ResolvedDependency<T> : IResolvedDependency where T : class
{
    private readonly Func<Node, (T value, Node provider)> _resolveFunc;
    private readonly Action<Type>? _onChanged;

    /// <summary>
    /// The currently resolved value.
    /// </summary>
    public T Value { get; private set; }

    /// <summary>
    /// The node that provided <see cref="Value"/>, either through
    /// <see cref="IProvide{T}"/> or by being <typeparamref name="T"/> itself.
    /// </summary>
    public Node Provider { get; private set; }

    /// <inheritdoc />
    public Type DependencyType => typeof(T);

    /// <summary>
    /// Creates a new resolved dependency entry.
    /// </summary>
    /// <param name="value">The resolved value.</param>
    /// <param name="provider">The node that supplied the value.</param>
    /// <param name="resolveFunc">
    /// The resolution function to run during revalidation. Receives the
    /// owner node and returns the resolved value and provider.
    /// </param>
    /// <param name="onChanged">
    /// Optional callback invoked when the provider's value changes or when
    /// revalidation detects a different provider.
    /// </param>
    internal ResolvedDependency(
        T value,
        Node provider,
        Func<Node, (T value, Node provider)> resolveFunc,
        Action<Type>? onChanged)
    {
        Value = value;
        Provider = provider;
        _resolveFunc = resolveFunc;
        _onChanged = onChanged;

        SubscribeToProvider();
    }

    /// <inheritdoc />
    public bool Revalidate(Node owner)
    {
        UnsubscribeFromProvider();

        var (newValue, newProvider) = _resolveFunc(owner);

        if (ReferenceEquals(newProvider, Provider))
        {
            // Same provider node. Value may or may not have changed,
            // but the provider relationship is intact.
            Value = newValue;
            SubscribeToProvider();
            return true;
        }

        // Provider changed, update everything.
        Provider = newProvider;
        Value = newValue;
        SubscribeToProvider();
        return false;
    }

    /// <inheritdoc />
    public void SubscribeToProvider()
    {
        if (Provider is IProvide<T> provide)
            provide.ProvidedValueChanged += OnProviderProvidedValueChanged;
    }

    /// <inheritdoc />
    public void UnsubscribeFromProvider()
    {
        if (Provider is IProvide<T> provide)
            provide.ProvidedValueChanged -= OnProviderProvidedValueChanged;
    }

    private void OnProviderProvidedValueChanged()
    {
        if (Provider is IProvide<T> provide)
            Value = provide.GetProvidedValue();

        _onChanged?.Invoke(DependencyType);
    }
}