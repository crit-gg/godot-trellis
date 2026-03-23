namespace GodotTrellis.Generator;

/// <summary>
/// Represents the containing class for a set of attributed properties.
/// </summary>
internal sealed class ClassModel
{
    /// <summary>The class name.</summary>
    public string ClassName { get; }

    /// <summary>The namespace, or null if in global namespace.</summary>
    public string? Namespace { get; }

    /// <summary>The accessibility modifier (public, internal, etc.).</summary>
    public string Accessibility { get; }

    /// <summary>All attributed properties on this class.</summary>
    public List<PropertyModel> Properties { get; }

    public ClassModel(
        string className,
        string? ns,
        string accessibility,
        List<PropertyModel> properties)
    {
        ClassName = className;
        Namespace = ns;
        Accessibility = accessibility;
        Properties = properties;
    }
}