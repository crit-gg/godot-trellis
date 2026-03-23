using Godot;
using twodog.xunit;

namespace GodotTrellis.Tests;

[Collection("GodotHeadless")]
public class DependencyResolverTests(GodotHeadlessFixture godot)
{
    [Fact]
    public void Resolve_ReturnsOverride_WithoutSceneTreeWalk()
    {
        var owner = new Node();
        var resolver = new DependencyResolver(owner);
        var expected = new TestService("override");

        resolver.Override<ITestService>(expected);

        var actual = resolver.Resolve<ITestService>(ResolveStrategy.Ancestor);

        Assert.Same(expected, actual);
    }

    [Fact]
    public void Resolve_OutsideTree_WithoutOverride_ThrowsInvalidOperationException()
    {
        var owner = new Node();
        var resolver = new DependencyResolver(owner);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve<ITestService>(ResolveStrategy.Ancestor));

        Assert.Contains("not inside the scene tree", ex.Message);
    }

    [Fact]
    public void Resolve_Ancestor_ReturnsNearestProvider()
    {
        WithTestRoot(testRoot =>
        {
            var farProvider = new TestProviderNode();
            farProvider.SetValue(new TestService("far"));
            testRoot.AddChild(farProvider);

            var nearProvider = new TestProviderNode();
            nearProvider.SetValue(new TestService("near"));
            farProvider.AddChild(nearProvider);

            var owner = new Node();
            nearProvider.AddChild(owner);

            var resolver = new DependencyResolver(owner);

            var resolved = resolver.Resolve<ITestService>(ResolveStrategy.Ancestor);

            Assert.Same(nearProvider.Current, resolved);
        });
    }

    [Fact]
    public void ResolveOwner_UsesNodeOwner_NotNearestAncestor()
    {
        WithTestRoot(testRoot =>
        {
            var ownerProvider = new TestProviderNode();
            ownerProvider.SetValue(new TestService("owner"));
            testRoot.AddChild(ownerProvider);

            var parentProvider = new TestProviderNode();
            parentProvider.SetValue(new TestService("parent"));
            ownerProvider.AddChild(parentProvider);

            var owner = new Node();
            parentProvider.AddChild(owner);

            parentProvider.Owner = ownerProvider;
            owner.Owner = ownerProvider;

            var resolver = new DependencyResolver(owner);

            var resolved = resolver.Resolve<ITestService>(ResolveStrategy.Owner);

            Assert.Same(ownerProvider.Current, resolved);
            Assert.NotSame(parentProvider.Current, resolved);
        });
    }

    [Fact]
    public void ResolveOptional_WhenMissing_ReturnsNull()
    {
        WithTestRoot(testRoot =>
        {
            var owner = new Node();
            testRoot.AddChild(owner);

            var resolver = new DependencyResolver(owner);

            var resolved = resolver.ResolveOptional<ITestService>(ResolveStrategy.Ancestor);

            Assert.Null(resolved);
        });
    }

    [Fact]
    public void Resolve_WhenMissingRequiredDependency_ThrowsDependencyNotFoundException()
    {
        WithTestRoot(testRoot =>
        {
            var owner = new Node();
            testRoot.AddChild(owner);

            var resolver = new DependencyResolver(owner);

            var ex = Assert.Throws<DependencyNotFoundException>(() =>
                resolver.Resolve<ITestService>(ResolveStrategy.Ancestor));

            Assert.Equal(typeof(Node), ex.OwnerType);
            Assert.Equal(typeof(ITestService), ex.DependencyType);
            Assert.Equal(ResolveStrategy.Ancestor, ex.Strategy);
        });
    }

    [Fact]
    public void Resolve_ChildDeepFalse_DoesNotSearchGrandchildren()
    {
        WithTestRoot(testRoot =>
        {
            var owner = new Node();
            testRoot.AddChild(owner);

            var intermediate = new Node();
            owner.AddChild(intermediate);

            var grandchildProvider = new TestProviderNode();
            grandchildProvider.SetValue(new TestService("grandchild"));
            intermediate.AddChild(grandchildProvider);

            var resolver = new DependencyResolver(owner);

            var resolved = resolver.ResolveOptional<ITestService>(ResolveStrategy.Child, deep: false);

            Assert.Null(resolved);
        });
    }

    [Fact]
    public void Resolve_ChildDeepTrue_FindsDescendant()
    {
        WithTestRoot(testRoot =>
        {
            var owner = new Node();
            testRoot.AddChild(owner);

            var intermediate = new Node();
            owner.AddChild(intermediate);

            var grandchildProvider = new TestProviderNode();
            grandchildProvider.SetValue(new TestService("grandchild"));
            intermediate.AddChild(grandchildProvider);

            var resolver = new DependencyResolver(owner);

            var resolved = resolver.Resolve<ITestService>(ResolveStrategy.Child, deep: true);

            Assert.Same(grandchildProvider.Current, resolved);
        });
    }

    [Fact]
    public void ResolveAll_ChildDeepTrue_ReturnsAllDescendantMatches()
    {
        WithTestRoot(testRoot =>
        {
            var owner = new Node();
            testRoot.AddChild(owner);

            var directProvider = new TestProviderNode();
            directProvider.SetValue(new TestService("direct"));
            owner.AddChild(directProvider);

            var intermediate = new Node();
            owner.AddChild(intermediate);

            var deepProvider = new TestProviderNode();
            deepProvider.SetValue(new TestService("deep"));
            intermediate.AddChild(deepProvider);

            var resolver = new DependencyResolver(owner);

            var resolved = resolver.ResolveAll<ITestService>(ResolveStrategy.Child, deep: true);

            Assert.Equal(2, resolved.Count);
            Assert.Same(directProvider.Current, resolved[0]);
            Assert.Same(deepProvider.Current, resolved[1]);
        });
    }

    [Fact]
    public void Resolve_Group_MultipleMatches_UsesFirstAndLogsWarning()
    {
        WithTestRoot(testRoot =>
        {
            var logs = new List<string>();
            DependencyResolver.Logger = logs.Add;
            try
            {
                var owner = new Node();
                testRoot.AddChild(owner);

                var firstProvider = new TestProviderNode();
                firstProvider.SetValue(new TestService("first"));
                testRoot.AddChild(firstProvider);
                firstProvider.AddToGroup("services");

                var secondProvider = new TestProviderNode();
                secondProvider.SetValue(new TestService("second"));
                testRoot.AddChild(secondProvider);
                secondProvider.AddToGroup("services");

                var resolver = new DependencyResolver(owner);

                var resolved = resolver.Resolve<ITestService>(
                    ResolveStrategy.Group,
                    groupName: "services");

                Assert.Same(firstProvider.Current, resolved);
                Assert.Contains(logs, line => line.Contains("WARNING:", StringComparison.Ordinal));
            }
            finally
            {
                DependencyResolver.Logger = null;
            }
        });
    }

    [Fact]
    public void Resolve_CachesValueUntilInvalidate()
    {
        WithTestRoot(testRoot =>
        {
            var provider = new TestProviderNode();
            provider.SetValue(new TestService("v1"));
            testRoot.AddChild(provider);

            var owner = new Node();
            provider.AddChild(owner);

            var resolver = new DependencyResolver(owner);

            var first = resolver.Resolve<ITestService>(ResolveStrategy.Ancestor);

            provider.SetValue(new TestService("v2"));
            var second = resolver.Resolve<ITestService>(ResolveStrategy.Ancestor);

            resolver.Invalidate();
            var third = resolver.Resolve<ITestService>(ResolveStrategy.Ancestor);

            Assert.Same(first, second);
            Assert.NotSame(first, third);
            Assert.Equal(2, provider.ValueCallCount);
        });
    }

    [Fact]
    public void ProviderChangedEvent_RefreshesCachedValueAndRaisesOnDependencyChanged()
    {
        WithTestRoot(testRoot =>
        {
            var provider = new TestProviderNode();
            provider.SetValue(new TestService("v1"));
            testRoot.AddChild(provider);

            var owner = new Node();
            provider.AddChild(owner);

            var resolver = new DependencyResolver(owner);

            Type? changedType = null;
            var changedCount = 0;
            resolver.OnDependencyChanged = type =>
            {
                changedType = type;
                changedCount++;
            };

            var first = resolver.Resolve<ITestService>(ResolveStrategy.Ancestor);

            var updated = new TestService("v2");
            provider.SetValue(updated, notify: true);

            var second = resolver.Resolve<ITestService>(ResolveStrategy.Ancestor);

            Assert.NotSame(first, second);
            Assert.Same(updated, second);
            Assert.Equal(1, changedCount);
            Assert.Equal(typeof(ITestService), changedType);
            Assert.Equal(2, provider.ValueCallCount);
        });
    }

    [Fact]
    public void Resolve_PrioritizesIProvideOverDirectTypeMatch()
    {
        WithTestRoot(testRoot =>
        {
            var hybrid = new DirectAndProviderNode();
            var provided = new TestService("provided");
            hybrid.SetProvidedValue(provided);
            testRoot.AddChild(hybrid);

            var owner = new Node();
            hybrid.AddChild(owner);

            var resolver = new DependencyResolver(owner);

            var resolved = resolver.Resolve<ITestService>(ResolveStrategy.Ancestor);

            Assert.Same(provided, resolved);
            Assert.NotSame(hybrid, resolved);
        });
    }

    [Fact]
    public void ClearOverrides_RemovesOverrideAndInvalidates()
    {
        var owner = new Node();
        var resolver = new DependencyResolver(owner);
        resolver.Override<ITestService>(new TestService("override"));

        resolver.ClearOverrides();

        Assert.Throws<InvalidOperationException>(() =>
            resolver.Resolve<ITestService>(ResolveStrategy.Ancestor));
    }

    private void WithTestRoot(Action<Node> testBody)
    {
        var testRoot = new Node
        {
            Name = $"TestRoot_{Guid.NewGuid():N}"
        };

        godot.Tree.Root.AddChild(testRoot);

        try
        {
            testBody(testRoot);
        }
        finally
        {
            if (testRoot.GetParent() is not null)
                testRoot.GetParent().RemoveChild(testRoot);

            testRoot.Free();
        }
    }

    private interface ITestService;

    private sealed class TestService(string id) : ITestService
    {
        public string Id { get; } = id;
    }

    private sealed class TestProviderNode : Node, IProvide<ITestService>
    {
        private ITestService _current = new TestService("default");

        public ITestService Current => _current;

        public int ValueCallCount { get; private set; }

        public ITestService GetProvidedValue()
        {
            ValueCallCount++;
            return _current;
        }

        public event Action? ProvidedValueChanged;

        public void SetValue(ITestService value, bool notify = false)
        {
            _current = value;
            if (notify)
                ProvidedValueChanged?.Invoke();
        }
    }

    private sealed class DirectAndProviderNode : Node, ITestService, IProvide<ITestService>
    {
        private ITestService _provided = new TestService("direct-default");

        public ITestService GetProvidedValue() => _provided;

        public event Action? ProvidedValueChanged;

        public void SetProvidedValue(ITestService value)
        {
            _provided = value;
            ProvidedValueChanged?.Invoke();
        }
    }
}