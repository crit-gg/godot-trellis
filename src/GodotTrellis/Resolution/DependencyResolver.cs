using Godot;

namespace GodotTrellis;

/// <summary>
/// Manages dependency resolution, caching, invalidation, and revalidation
/// for a single Godot node. Each node that uses <c>[From*]</c> attributes
/// gets one <see cref="DependencyResolver"/> instance, created by generated code.
/// <para>
/// The resolver is lazy, no resolution occurs until a dependency property
/// is first accessed. Resolved values are cached and automatically invalidated
/// when the owner node exits the scene tree.
/// </para>
/// </summary>
public sealed class DependencyResolver
{
    private readonly Node _owner;
    private readonly Dictionary<Type, IResolvedDependency> _entries = new();
    private Dictionary<Type, object>? _overrides;
    private bool _subscribed;
    private bool _dirty;

    /// <summary>
    /// Invoked when a specific dependency changes, either because the provider
    /// node changed after reparenting, or because the provider fired its
    /// <see cref="IProvide{T}.ProvidedValueChanged"/> event. The argument is the dependency type.
    /// </summary>
    public Action<Type>? OnDependencyChanged;

    /// <summary>
    /// Invoked after <see cref="Revalidate"/> completes, once all individual
    /// <see cref="OnDependencyChanged"/> callbacks have fired.
    /// </summary>
    public Action? OnRevalidated;

    /// <summary>
    /// Optional global logger. When set, the resolver logs resolution walks,
    /// cache hits/misses, invalidation events, and warnings.
    /// Set to <c>null</c> in production for zero overhead.
    /// </summary>
    public static Action<string>? Logger;

    public DependencyResolver(Node owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Resolves a single required dependency.
    /// </summary>
    public T Resolve<T>(
        ResolveStrategy strategy,
        string? groupName = null,
        bool deep = false,
        bool useSceneFilePath = false) where T : class
    {
        EnsureSubscribed();

        if (TryGetOverride<T>(out var overrideValue))
            return overrideValue;

        if (!_dirty && _entries.TryGetValue(typeof(T), out var existing))
        {
            var cached = (ResolvedDependency<T>)existing;
            Log($"Cache hit: {typeof(T).Name} on {_owner.Name}");
            return cached.Value;
        }

        var (value, provider) = RunResolution<T>(strategy, groupName, deep, useSceneFilePath);
        CacheEntry(value, provider, strategy, groupName, deep, useSceneFilePath);
        return value;
    }

    /// <summary>
    /// Resolves a single optional dependency. Returns <c>null</c> if not found.
    /// </summary>
    public T? ResolveOptional<T>(
        ResolveStrategy strategy,
        string? groupName = null,
        bool deep = false,
        bool useSceneFilePath = false) where T : class
    {
        EnsureSubscribed();

        if (TryGetOverride<T>(out var overrideValue))
            return overrideValue;

        if (!_dirty && _entries.TryGetValue(typeof(T), out var existing))
        {
            var cached = (ResolvedDependency<T>)existing;
            Log($"Cache hit: {typeof(T).Name} on {_owner.Name}");
            return cached.Value;
        }

        var result = TryRunResolution<T>(strategy, groupName, deep, useSceneFilePath);
        if (result is null)
        {
            Log($"Optional resolution returned null: {typeof(T).Name} on {_owner.Name}");
            return null;
        }

        var (value, provider) = result.Value;
        CacheEntry(value, provider, strategy, groupName, deep, useSceneFilePath);
        return value;
    }

    /// <summary>
    /// Resolves all matching dependencies as a collection.
    /// </summary>
    public IReadOnlyList<T> ResolveAll<T>(
        ResolveStrategy strategy,
        string? groupName = null,
        bool deep = false,
        bool useSceneFilePath = false) where T : class
    {
        EnsureSubscribed();

        // Overrides not supported for collections — resolve normally.

        if (!_dirty && _entries.TryGetValue(typeof(IEnumerable<T>), out var existing))
        {
            var cached = (ResolvedCollection<T>)existing;
            Log($"Cache hit (collection): {typeof(T).Name} on {_owner.Name}");
            return cached.Values;
        }

        var entries = RunCollectionResolution<T>(strategy, groupName, deep, useSceneFilePath);
        CacheCollectionEntry(entries, strategy, groupName, deep, useSceneFilePath);
        return entries.Select(e => e.value).ToList();
    }

    /// <summary>
    /// Eagerly revalidates all cached dependencies against the current scene
    /// tree. Fires <see cref="OnDependencyChanged"/> for each dependency whose
    /// provider has changed, then fires <see cref="OnRevalidated"/>.
    /// </summary>
    public void Revalidate()
    {
        if (!_dirty) return;
        _dirty = false;

        foreach (var entry in _entries.Values)
        {
            if (!entry.Revalidate(_owner))
                OnDependencyChanged?.Invoke(entry.DependencyType);
        }

        OnRevalidated?.Invoke();
        Log($"Revalidation complete on {_owner.Name}");
    }

    /// <summary>
    /// Clears the entire cache. Use for object pooling scenarios where a node
    /// is recycled without leaving the tree.
    /// </summary>
    public void Invalidate()
    {
        UnsubscribeAll();
        _entries.Clear();
        _dirty = false;
        Log($"Manual invalidation on {_owner.Name}");
    }

    /// <summary>
    /// Registers a test override for a dependency type. When an override
    /// exists, <see cref="Resolve{T}"/> returns it immediately with no
    /// tree walk, subscription, or caching.
    /// </summary>
    public void Override<T>(T value) where T : class
    {
        _overrides ??= new Dictionary<Type, object>();
        _overrides[typeof(T)] = value;
    }

    /// <summary>
    /// Removes all test overrides and clears the cache.
    /// </summary>
    public void ClearOverrides()
    {
        _overrides = null;
        Invalidate();
    }

    private void EnsureSubscribed()
    {
        if (_subscribed) return;
        _subscribed = true;
        _owner.TreeExiting += OnTreeExiting;
        _owner.TreeExited += OnTreeExited;
    }

    private void OnTreeExiting()
    {
        // Keep cached values available for user _ExitTree callbacks.
        Log($"Tree exiting on {_owner.Name}");
    }

    private void OnTreeExited()
    {
        UnsubscribeAll();
        _entries.Clear();
        _dirty = true;
        Log($"Tree exited, cache cleared on {_owner.Name}");
    }

    private void UnsubscribeAll()
    {
        foreach (var entry in _entries.Values)
            entry.UnsubscribeFromProvider();
    }

    private (T value, Node provider) RunResolution<T>(
        ResolveStrategy strategy,
        string? groupName,
        bool deep,
        bool useSceneFilePath) where T : class
    {
        var result = TryRunResolution<T>(strategy, groupName, deep, useSceneFilePath);

        if (result is null)
        {
            throw new DependencyNotFoundException(
                _owner.GetType(), typeof(T), strategy);
        }

        return result.Value;
    }

    private (T value, Node provider)? TryRunResolution<T>(
        ResolveStrategy strategy,
        string? groupName,
        bool deep,
        bool useSceneFilePath) where T : class
    {
        EnsureInTree();

        Log($"Resolving {typeof(T).Name} via {strategy} on {_owner.Name}");

        return strategy switch
        {
            ResolveStrategy.Ancestor => WalkAncestors<T>(),
            ResolveStrategy.Owner => CheckOwner<T>(useSceneFilePath),
            ResolveStrategy.Group => QueryGroup<T>(groupName!),
            ResolveStrategy.Child => SearchChildren<T>(deep),
            ResolveStrategy.Sibling => SearchSiblings<T>(),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null),
        };
    }

    private (T value, Node provider)? WalkAncestors<T>() where T : class
    {
        var current = _owner.GetParent();
        var depth = 0;

        while (current is not null)
        {
            depth++;
            if (TryMatch<T>(current, out var value))
            {
                Log($"Found {typeof(T).Name} at depth {depth}: {current.Name}");
                return (value, current);
            }
            current = current.GetParent();
        }

        Log($"No {typeof(T).Name} found in ancestors (walked {depth} nodes)");
        return null;
    }

    private (T value, Node provider)? CheckOwner<T>(bool useSceneFilePath) where T : class
    {
        if (useSceneFilePath)
        {
            var current = _owner.GetParent();
            while (current is not null)
            {
                if (!string.IsNullOrEmpty(current.SceneFilePath))
                {
                    if (TryMatch<T>(current, out var value))
                    {
                        Log($"Found {typeof(T).Name} at scene root: {current.Name}");
                        return (value, current);
                    }

                    // Found a scene root but it doesn't provide T. Stop here
                    // for strict scene scoping.
                    Log($"Scene root {current.Name} does not provide {typeof(T).Name}");
                    return null;
                }
                current = current.GetParent();
            }

            Log($"No scene root found providing {typeof(T).Name}");
            return null;
        }

        var owner = _owner.Owner;
        if (owner is null)
        {
            Log($"Owner is null on {_owner.Name}");
            return null;
        }

        if (TryMatch<T>(owner, out var ownerValue))
        {
            Log($"Found {typeof(T).Name} on owner: {owner.Name}");
            return (ownerValue, owner);
        }

        Log($"Owner {owner.Name} does not provide {typeof(T).Name}");
        return null;
    }

    private (T value, Node provider)? QueryGroup<T>(string groupName) where T : class
    {
        var nodes = _owner.GetTree().GetNodesInGroup(groupName);
        (T value, Node provider)? firstMatch = null;
        var matchCount = 0;

        foreach (var node in nodes)
        {
            if (TryMatch<T>(node, out var value))
            {
                matchCount++;
                firstMatch ??= (value, node);
            }
        }

        if (matchCount > 1)
        {
            Log($"WARNING: {matchCount} nodes in group '{groupName}' provide {typeof(T).Name}, using first match: {firstMatch!.Value.provider.Name}");
        }
        else if (firstMatch is not null)
        {
            Log($"Found {typeof(T).Name} in group '{groupName}': {firstMatch.Value.provider.Name}");
        }
        else
        {
            Log($"No {typeof(T).Name} found in group '{groupName}'");
        }

        return firstMatch;
    }

    private (T value, Node provider)? SearchChildren<T>(bool deep) where T : class
    {
        return deep
            ? SearchChildrenDeep<T>(_owner)
            : SearchChildrenDirect<T>();
    }

    private (T value, Node provider)? SearchSiblings<T>() where T : class
    {
        var parent = _owner.GetParent();
        if (parent is null)
        {
            Log($"No parent available, cannot search siblings for {typeof(T).Name}");
            return null;
        }

        foreach (var sibling in parent.GetChildren())
        {
            if (ReferenceEquals(sibling, _owner))
                continue;

            if (TryMatch<T>(sibling, out var value))
            {
                Log($"Found {typeof(T).Name} in sibling: {sibling.Name}");
                return (value, sibling);
            }
        }

        Log($"No {typeof(T).Name} found in siblings");
        return null;
    }

    private (T value, Node provider)? SearchChildrenDirect<T>() where T : class
    {
        foreach (var child in _owner.GetChildren())
        {
            if (TryMatch<T>(child, out var value))
            {
                Log($"Found {typeof(T).Name} in direct child: {child.Name}");
                return (value, child);
            }
        }

        Log($"No {typeof(T).Name} found in direct children");
        return null;
    }

    private (T value, Node provider)? SearchChildrenDeep<T>(Node parent) where T : class
    {
        foreach (var child in parent.GetChildren())
        {
            if (TryMatch<T>(child, out var value))
            {
                Log($"Found {typeof(T).Name} in descendant: {child.Name}");
                return (value, child);
            }

            var deeper = SearchChildrenDeep<T>(child);
            if (deeper is not null) return deeper;
        }

        return null;
    }

    private List<(T value, Node provider)> RunCollectionResolution<T>(
        ResolveStrategy strategy,
        string? groupName,
        bool deep,
        bool useSceneFilePath) where T : class
    {
        EnsureInTree();

        Log($"Resolving collection {typeof(T).Name} via {strategy} on {_owner.Name}");

        return strategy switch
        {
            ResolveStrategy.Ancestor => CollectAncestors<T>(),
            ResolveStrategy.Owner => CollectOwner<T>(useSceneFilePath),
            ResolveStrategy.Group => CollectGroup<T>(groupName!),
            ResolveStrategy.Child => CollectChildren<T>(deep),
            ResolveStrategy.Sibling => CollectSiblings<T>(),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null),
        };
    }

    private List<(T value, Node provider)> CollectAncestors<T>() where T : class
    {
        var results = new List<(T, Node)>();
        var current = _owner.GetParent();

        while (current is not null)
        {
            if (TryMatch<T>(current, out var value))
                results.Add((value, current));
            current = current.GetParent();
        }

        Log($"Collected {results.Count} {typeof(T).Name} from ancestors");
        return results;
    }

    private List<(T value, Node provider)> CollectOwner<T>(bool useSceneFilePath) where T : class
    {
        var results = new List<(T, Node)>();

        if (useSceneFilePath)
        {
            var current = _owner.GetParent();
            while (current is not null)
            {
                if (!string.IsNullOrEmpty(current.SceneFilePath) &&
                    TryMatch<T>(current, out var value))
                {
                    results.Add((value, current));
                }
                current = current.GetParent();
            }
        }
        else
        {
            var owner = _owner.Owner;
            if (owner is not null && TryMatch<T>(owner, out var value))
                results.Add((value, owner));
        }

        Log($"Collected {results.Count} {typeof(T).Name} from owner");
        return results;
    }

    private List<(T value, Node provider)> CollectGroup<T>(string groupName) where T : class
    {
        var results = new List<(T, Node)>();
        var nodes = _owner.GetTree().GetNodesInGroup(groupName);

        foreach (var node in nodes)
        {
            if (TryMatch<T>(node, out var value))
                results.Add((value, node));
        }

        Log($"Collected {results.Count} {typeof(T).Name} from group '{groupName}'");
        return results;
    }

    private List<(T value, Node provider)> CollectChildren<T>(bool deep) where T : class
    {
        var results = new List<(T, Node)>();

        if (deep)
            CollectChildrenDeep(_owner, results);
        else
            CollectChildrenDirect(results);

        Log($"Collected {results.Count} {typeof(T).Name} from children (deep={deep})");
        return results;
    }

    private List<(T value, Node provider)> CollectSiblings<T>() where T : class
    {
        var results = new List<(T, Node)>();
        var parent = _owner.GetParent();

        if (parent is null)
        {
            Log($"No parent available, collected 0 {typeof(T).Name} from siblings");
            return results;
        }

        foreach (var sibling in parent.GetChildren())
        {
            if (ReferenceEquals(sibling, _owner))
                continue;

            if (TryMatch<T>(sibling, out var value))
                results.Add((value, sibling));
        }

        Log($"Collected {results.Count} {typeof(T).Name} from siblings");
        return results;
    }

    private void CollectChildrenDirect<T>(List<(T, Node)> results) where T : class
    {
        foreach (var child in _owner.GetChildren())
        {
            if (TryMatch<T>(child, out var value))
                results.Add((value, child));
        }
    }

    private static void CollectChildrenDeep<T>(Node parent, List<(T, Node)> results) where T : class
    {
        foreach (var child in parent.GetChildren())
        {
            if (TryMatch<T>(child, out var value))
                results.Add((value, child));

            CollectChildrenDeep(child, results);
        }
    }

    private void CacheEntry<T>(
        T value,
        Node provider,
        ResolveStrategy strategy,
        string? groupName,
        bool deep,
        bool useSceneFilePath) where T : class
    {
        // Remove old entry if re-resolving after dirty.
        if (_entries.TryGetValue(typeof(T), out var old))
            old.UnsubscribeFromProvider();

        var resolveFunc = BuildResolveFunc<T>(strategy, groupName, deep, useSceneFilePath);

        var entry = new ResolvedDependency<T>(
            value, provider, resolveFunc, OnDependencyChanged);

        _entries[typeof(T)] = entry;
        _dirty = false;
    }

    private void CacheCollectionEntry<T>(
        List<(T value, Node provider)> entries,
        ResolveStrategy strategy,
        string? groupName,
        bool deep,
        bool useSceneFilePath) where T : class
    {
        var cacheKey = typeof(IEnumerable<T>);

        if (_entries.TryGetValue(cacheKey, out var old))
            old.UnsubscribeFromProvider();

        var resolveFunc = BuildCollectionResolveFunc<T>(strategy, groupName, deep, useSceneFilePath);

        var collection = new ResolvedCollection<T>(
            entries, resolveFunc, OnDependencyChanged);

        _entries[cacheKey] = collection;
        _dirty = false;
    }

    private Func<Node, (T value, Node provider)> BuildResolveFunc<T>(
        ResolveStrategy strategy,
        string? groupName,
        bool deep,
        bool useSceneFilePath) where T : class
    {
        return strategy switch
        {
            ResolveStrategy.Ancestor => owner => WalkAncestors<T>()
                ?? throw new DependencyNotFoundException(owner.GetType(), typeof(T), strategy),
            ResolveStrategy.Owner => owner => CheckOwner<T>(useSceneFilePath)
                ?? throw new DependencyNotFoundException(owner.GetType(), typeof(T), strategy),
            ResolveStrategy.Group => owner => QueryGroup<T>(groupName!)
                ?? throw new DependencyNotFoundException(owner.GetType(), typeof(T), strategy),
            ResolveStrategy.Child => owner => SearchChildren<T>(deep)
                ?? throw new DependencyNotFoundException(owner.GetType(), typeof(T), strategy),
            ResolveStrategy.Sibling => owner => SearchSiblings<T>()
                ?? throw new DependencyNotFoundException(owner.GetType(), typeof(T), strategy),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
        };
    }

    private Func<Node, List<(T value, Node provider)>> BuildCollectionResolveFunc<T>(
        ResolveStrategy strategy,
        string? groupName,
        bool deep,
        bool useSceneFilePath) where T : class
    {
        return strategy switch
        {
            ResolveStrategy.Ancestor => _ => CollectAncestors<T>(),
            ResolveStrategy.Owner => _ => CollectOwner<T>(useSceneFilePath),
            ResolveStrategy.Group => _ => CollectGroup<T>(groupName!),
            ResolveStrategy.Child => _ => CollectChildren<T>(deep),
            ResolveStrategy.Sibling => _ => CollectSiblings<T>(),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
        };
    }

    private static bool TryMatch<T>(Node node, out T value) where T : class
    {
        // IProvide<T> takes priority over direct type match.
        if (node is IProvide<T> provider)
        {
            value = provider.GetProvidedValue();
            return true;
        }

        if (node is T direct)
        {
            value = direct;
            return true;
        }

        value = default!;
        return false;
    }

    private bool TryGetOverride<T>(out T value) where T : class
    {
        if (_overrides is not null && _overrides.TryGetValue(typeof(T), out var obj))
        {
            value = (T)obj;
            return true;
        }

        value = default!;
        return false;
    }

    private void EnsureInTree()
    {
        if (!_owner.IsInsideTree())
        {
            throw new InvalidOperationException(
                $"{_owner.GetType().Name} attempted to resolve a dependency " +
                $"but is not inside the scene tree.");
        }
    }

    private static void Log(string message)
    {
        Logger?.Invoke($"[GodotTrellis] {message}");
    }
}
