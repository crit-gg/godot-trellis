using Microsoft.CodeAnalysis;

namespace GodotTrellis.Generator;

internal static class DiagnosticDescriptors
{
    private const string Category = "GodotTrellis";

    /// <summary>
    /// GTR001: [From*] attribute on a non-partial property.
    /// </summary>
    public static readonly DiagnosticDescriptor NonPartialProperty = new(
        id: "GTR001",
        title: "Property must be partial",
        messageFormat: "Property '{0}' has a [From*] attribute but is not declared as partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Properties using GodotTrellis resolution attributes must be declared as partial so the source generator can emit the implementation.");

    /// <summary>
    /// GTR002: [From*] attribute on a property in a class that doesn't inherit from Godot.Node.
    /// </summary>
    public static readonly DiagnosticDescriptor NotANodeClass = new(
        id: "GTR002",
        title: "Containing class must inherit from Godot.Node",
        messageFormat: "Property '{0}' has a [From*] attribute but its containing class '{1}' does not inherit from Godot.Node",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "GodotTrellis resolution attributes can only be used on properties in classes that inherit from Godot.Node, as the resolver requires scene tree access.");

    /// <summary>
    /// GTR003: [FromGroup] used without a group name parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingGroupName = new(
        id: "GTR003",
        title: "FromGroup requires a group name",
        messageFormat: "Property '{0}' uses [FromGroup] without specifying a group name",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The [FromGroup] attribute requires a group name as a constructor argument, e.g. [FromGroup(\"services\")].");

    /// <summary>
    /// GTR004: Required = false on a non-nullable property type.
    /// </summary>
    public static readonly DiagnosticDescriptor OptionalButNotNullable = new(
        id: "GTR004",
        title: "Optional dependency must use nullable type",
        messageFormat: "Property '{0}' is marked as optional (Required = false) but its type '{1}' is not nullable",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Properties with Required = false may return null, so the property type must be declared as nullable (e.g. ILogger? instead of ILogger).");

    /// <summary>
    /// GTR005: Multiple [From*] attributes on the same property.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleFromAttributes = new(
        id: "GTR005",
        title: "Multiple resolution attributes on the same property",
        messageFormat: "Property '{0}' has multiple [From*] attributes; only one resolution strategy is allowed per property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Each property should have exactly one resolution attribute. If multiple are present, the first one found will be used.");
}