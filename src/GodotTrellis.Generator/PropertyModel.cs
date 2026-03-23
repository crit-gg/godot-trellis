namespace GodotTrellis.Generator;

/// <summary>
/// Represents the extracted data from a single [From*] attributed partial property.
/// </summary>
internal sealed class PropertyModel
{
    /// <summary>The property name.</summary>
    public string PropertyName { get; }

    /// <summary>The fully qualified type name of the property.</summary>
    public string TypeName { get; }

    /// <summary>
    /// For collection properties (IEnumerable&lt;T&gt;), the inner type name.
    /// Null for single-value properties.
    /// </summary>
    public string? CollectionElementTypeName { get; }

    /// <summary>Whether this is a collection (IEnumerable&lt;T&gt;) property.</summary>
    public bool IsCollection => CollectionElementTypeName is not null;

    /// <summary>Whether the property type is nullable (Required = false).</summary>
    public bool IsOptional { get; }

    /// <summary>The resolution strategy.</summary>
    public ResolveStrategyModel Strategy { get; }

    /// <summary>For FromGroup: the group name. Null otherwise.</summary>
    public string? GroupName { get; }

    /// <summary>For FromChild: whether deep search is enabled.</summary>
    public bool Deep { get; }

    /// <summary>For FromOwner: whether to use SceneFilePath.</summary>
    public bool UseSceneFilePath { get; }

    /// <summary> The access modifier (public, internal, etc.) of the property. </summary>
    public string AccessModifier { get; }

    public PropertyModel(
        string propertyName,
        string typeName,
        string? collectionElementTypeName,
        bool isOptional,
        ResolveStrategyModel strategy,
        string? groupName,
        bool deep,
        bool useSceneFilePath,
        string accessModifier)
    {
        PropertyName = propertyName;
        TypeName = typeName;
        CollectionElementTypeName = collectionElementTypeName;
        IsOptional = isOptional;
        Strategy = strategy;
        GroupName = groupName;
        Deep = deep;
        UseSceneFilePath = useSceneFilePath;
        AccessModifier = accessModifier;
    }
}