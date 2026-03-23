namespace GodotTrellis;

/// <summary>
/// Base class for all GodotTrellis resolution attributes.
/// Cannot be used directly, use one of the derived attributes:
/// <see cref="FromAncestorAttribute"/>, <see cref="FromOwnerAttribute"/>,
/// <see cref="FromGroupAttribute"/>, or <see cref="FromChildAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public abstract class FromAttribute : Attribute
{
    /// <summary>
    /// If <c>true</c> (default), resolution throws
    /// <see cref="DependencyNotFoundException"/> when no provider is found.
    /// If <c>false</c>, the property returns <c>null</c> and must be declared
    /// with a nullable type.
    /// </summary>
    public bool Required { get; set; } = true;
}