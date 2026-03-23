# GodotTrellis

**Lightweight, source-generated service location for Godot 4.5 and C# 14.**

Resolve dependencies through the scene tree using partial properties and attributes. No base classes, no `_Notification` overrides, no lifecycle takeover. Drop it into an existing project and start using it one node at a time.

```csharp
public partial class PlayerHUD : Control
{
    [FromAncestor]
    public partial IScoreTracker Score { get; }

    [FromGroup("services")]
    public partial IAudioService Audio { get; }

    [FromChild]
    public partial HealthBar Health { get; }
}
```

No `_Ready` setup. No `_Notification` override. Properties resolve lazily on first access and cache automatically.

---

## Features

- Zero boilerplate. Declare a partial property, get a resolved dependency.
- Multiple resolution strategies: ancestors, scene owner, groups, children.
- Lazy resolution. First access walks the tree, subsequent accesses hit the cache.
- Automatic invalidation. Cache clears on tree exit, re-resolves on re-entry.
- Provider change tracking. Providers signal value changes, dependents update automatically.
- Test-friendly. Override any dependency without a scene tree.

### Requirements

- **Godot 4.5** or later
- **.NET 10** or later
- **C# 14** (required for partial properties)

GodotTrellis uses C# 14 partial properties as its core API. These are not available in earlier C# versions. If your project targets .NET 8 or .NET 9, you'll need to upgrade before using this library.

---

## Installation

Add both packages to your `.csproj`:

- GodotTrellis
- GodotTrellis.Generator

---

## Quick Start

### 1. Make a node provide something

Implement `IProvide<T>` on any node that should supply a value to other nodes:

```csharp
using GodotTrellis;

public partial class AppRoot : Node, IProvide<ILogger>
{
    private readonly ILogger _logger = new ConsoleLogger();

    public ILogger Value() => _logger;
}
```

That's all a static provider needs. The `Changed` event has a default no-op implementation, so you don't need to declare it unless your value can change at runtime.

### 2. Consume it from a descendant

Add a `[FromAncestor]` partial property on any node below the provider in the scene tree:

```csharp
using GodotTrellis;

public partial class SomeNode : Node3D
{
    [FromAncestor]
    public partial ILogger Logger { get; }

    public override void _Ready()
    {
        Logger.Log("Hello from the tree!");
    }
}
```

The source generator writes the property implementation for you. At runtime, the first time you access `Logger`, GodotTrellis walks up the tree, finds the nearest `IProvide<ILogger>`, caches it, and returns the value.

### 3. Run your scene

```
AppRoot              ← provides ILogger
├── World
│   └── SomeNode     ← Logger resolves here
└── UI
    └── OtherNode    ← Logger also works here
```

No registration step. No container. If the provider is above you in the tree, it's found.

---

## Resolution Strategies

### `[FromAncestor]`

Finds the nearest ancestor that provides the requested type.

```csharp
[FromAncestor]
public partial ILogger Logger { get; }
```

### `[FromOwner]`

Resolves from the node's `Owner` (the root of the scene it belongs to).

```csharp
[FromOwner]
public partial SceneController Controller { get; }
```

With `UseSceneFilePath = true`, walks up to the nearest node that was instanced from a `PackedScene` instead. Useful for nested instanced scenes where `Owner` might skip past your immediate scene boundary:

```csharp
[FromOwner(UseSceneFilePath = true)]
public partial IResourceLoader Loader { get; }
```

### `[FromGroup("name")]`

Resolves from any node in the named Godot group, regardless of where it sits in the tree. No tree walking; it queries the group directly.

```csharp
[FromGroup("services")]
public partial IAudioService Audio { get; }
```

The group name is required. Nodes join groups via `AddToGroup("services")` in code or through the Godot editor.

If multiple nodes in the group provide the same type, the first match is used and a warning is logged. Use `IEnumerable<T>` to collect all matches (see [Collections](#collections)).

### `[FromChild]`

Finds the first direct child that matches.

```csharp
[FromChild]
public partial HealthBar Health { get; }
```

Set `Deep = true` to search all descendants recursively:

```csharp
[FromChild(Deep = true)]
public partial InteractableComponent Interactable { get; }
```

---

## Collections

Any strategy can return multiple results. Declare the property as `IEnumerable<T>` or `IReadOnlyList<T>`:

```csharp
[FromAncestor]
public partial IEnumerable<IBuffProvider> ActiveBuffs { get; }

[FromChild(Deep = true)]
public partial IReadOnlyList<InteractableComponent> Interactables { get; }

[FromGroup("ui")]
public partial IEnumerable<ITooltipProvider> Tooltips { get; }
```

Collections collect all matches for the given strategy. No "multiple match" warning is logged.

---

## Optional Dependencies

By default, a missing dependency throws `DependencyNotFoundException`. Set `Required = false` and use a nullable type for optional dependencies:

```csharp
[FromAncestor(Required = false)]
public partial IEventBus? EventBus { get; }
```

If no provider is found, the property returns `null` instead of throwing.

---

## Providing Values

### Static Provider

Value never changes. Just implement `Value()`:

```csharp
public partial class ServiceRoot : Node, IProvide<ILogger>, IProvide<IConfigProvider>
{
    private readonly ILogger _logger = new FileLogger();
    private readonly IConfigProvider _config = new JsonConfig();

    public ILogger Value() => _logger;
    IConfigProvider IProvide<IConfigProvider>.Value() => _config;
}
```

A single node can provide multiple types.

### Dynamic Provider

If the value can change at runtime, declare the `Changed` event and fire it when the value updates:

```csharp
public partial class ThemeManager : Node, IProvide<ITheme>
{
    private ITheme _current = new DefaultTheme();

    public ITheme Value() => _current;
    public new event Action? Changed;

    public void SetTheme(ITheme theme)
    {
        _current = theme;
        Changed?.Invoke();
    }
}
```

When `Changed` fires, any node that resolved this dependency automatically re-pulls the value. No tree walk occurs; only the cached value is refreshed.

### Direct Type Match

A node doesn't have to implement `IProvide<T>`. If the node *is* the requested type, it matches directly:

```csharp
public partial class Lever : Node3D, IInteractable
{
    public void Interact() { /* ... */ }
}

public partial class Room : Node3D
{
    [FromChild(Deep = true)]
    public partial IEnumerable<IInteractable> Interactables { get; }
}
```

When both `IProvide<T>` and a direct type match exist on the same node, `IProvide<T>` takes priority.
