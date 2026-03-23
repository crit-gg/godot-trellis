using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GodotTrellis.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class TrellisSourceGenerator : IIncrementalGenerator
{
    private static readonly string[] FromAttributeNames =
    {
        "FromAncestor",
        "FromAncestorAttribute",
        "FromOwner",
        "FromOwnerAttribute",
        "FromGroup",
        "FromGroupAttribute",
        "FromChild",
        "FromChildAttribute",
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var propertyInfos = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidate(node),
                transform: static (ctx, ct) => ExtractPropertyInfo(ctx, ct))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        var collected = propertyInfos.Collect();

        context.RegisterSourceOutput(collected, static (spc, properties) =>
        {
            // Report all diagnostics first.
            foreach (var prop in properties)
            {
                if (prop.Diagnostics is not null)
                {
                    foreach (var diag in prop.Diagnostics)
                        spc.ReportDiagnostic(diag);
                }
            }

            // Group valid properties by class and emit source.
            var groups = properties
                .Where(p => !p.HasErrors)
                .GroupBy(p => (p.ClassName, p.Namespace));

            foreach (var group in groups)
            {
                var first = group.First();
                var propModels = group.Select(p => p.Property).ToList();

                var model = new ClassModel(
                    first.ClassName,
                    first.Namespace,
                    first.Accessibility,
                    propModels);

                var source = Emitter.Emit(model);
                var hintName = model.Namespace is not null
                    ? $"{model.Namespace}.{model.ClassName}.g.cs"
                    : $"{model.ClassName}.g.cs";

                spc.AddSource(hintName, source);
            }
        });
    }

    /// <summary>
    /// Fast syntactic check — does this node look like a property with a From* attribute?
    /// </summary>
    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not PropertyDeclarationSyntax property)
            return false;

        foreach (var attrList in property.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = GetAttributeName(attr);
                if (name is not null && IsFromAttribute(name))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Semantic analysis of a candidate property.
    /// </summary>
    private static PropertyInfo? ExtractPropertyInfo(
        GeneratorSyntaxContext context,
        CancellationToken ct)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        var propertySymbol = semanticModel.GetDeclaredSymbol(property, ct);
        if (propertySymbol is not IPropertySymbol propSymbol)
            return null;

        // Find From* attributes.
        var fromAttributes = new List<(AttributeData attr, ResolveStrategyModel strategy)>();

        foreach (var attrData in propSymbol.GetAttributes())
        {
            var attrClass = attrData.AttributeClass;
            if (attrClass is null) continue;

            if (TryGetStrategy(attrClass.Name, out var strategy))
            {
                // Verify it's our attribute from the GodotTrellis namespace.
                var cns = attrClass.ContainingNamespace?.ToDisplayString();
                if (cns == "GodotTrellis")
                    fromAttributes.Add((attrData, strategy));
            }
        }

        if (fromAttributes.Count == 0)
            return null;

        List<Diagnostic>? diagnostics = null;
        var hasErrors = false;

        // GTR005: Multiple From* attributes.
        if (fromAttributes.Count > 1)
        {
            diagnostics ??= new List<Diagnostic>();
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.MultipleFromAttributes,
                property.GetLocation(),
                propSymbol.Name));
        }

        var (primaryAttr, primaryStrategy) = fromAttributes[0];

        // GTR001: Property must be partial.
        if (!property.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics ??= new List<Diagnostic>();
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.NonPartialProperty,
                property.GetLocation(),
                propSymbol.Name));
            hasErrors = true;
        }

        // GTR002: Containing class must inherit from Godot.Node.
        var containingType = propSymbol.ContainingType;
        if (!InheritsFromGodotNode(containingType))
        {
            diagnostics ??= new List<Diagnostic>();
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.NotANodeClass,
                property.GetLocation(),
                propSymbol.Name,
                containingType.Name));
            hasErrors = true;
        }

        // Extract attribute parameters.
        var required = GetNamedArgBool(primaryAttr, "Required") ?? true;
        var groupName = GetConstructorArgString(primaryAttr, 0);
        var deep = GetNamedArgBool(primaryAttr, "Deep") ?? false;
        var useSceneFilePath = GetNamedArgBool(primaryAttr, "UseSceneFilePath") ?? false;

        // GTR003: FromGroup must have a group name.
        if (primaryStrategy == ResolveStrategyModel.Group && string.IsNullOrEmpty(groupName))
        {
            diagnostics ??= new List<Diagnostic>();
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.MissingGroupName,
                property.GetLocation(),
                propSymbol.Name));
            hasErrors = true;
        }

        // Type analysis.
        var propertyType = propSymbol.Type;
        var isNullable = propertyType.NullableAnnotation == NullableAnnotation.Annotated;
        var isOptional = !required;

        // GTR004: Optional but not nullable.
        if (isOptional && !isNullable)
        {
            diagnostics ??= new List<Diagnostic>();
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.OptionalButNotNullable,
                property.GetLocation(),
                propSymbol.Name,
                propertyType.ToDisplayString()));
            hasErrors = true;
        }

        // Detect IEnumerable<T> / IReadOnlyList<T>.
        string? collectionElementTypeName = null;
        var typeName = propertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (IsCollectionType(propertyType, out var elementType))
        {
            collectionElementTypeName = elementType!.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat);
        }

        // Containing class info.
        var className = containingType.Name;
        var ns = containingType.ContainingNamespace?.IsGlobalNamespace == true
            ? null
            : containingType.ContainingNamespace?.ToDisplayString();
        var accessibility = AccessibilityToString(containingType.DeclaredAccessibility);
        var propertyAccessibility = AccessibilityToString(propSymbol.DeclaredAccessibility);

        var propModel = new PropertyModel(
            propertyName: propSymbol.Name,
            typeName: typeName,
            collectionElementTypeName: collectionElementTypeName,
            isOptional: isOptional,
            strategy: primaryStrategy,
            groupName: groupName,
            deep: deep,
            useSceneFilePath: useSceneFilePath,
            propertyAccessibility);

        return new PropertyInfo(
            className, ns, accessibility, propModel, diagnostics, hasErrors);
    }

    // ────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────

    private static string? GetAttributeName(AttributeSyntax attr)
    {
        return attr.Name switch
        {
            SimpleNameSyntax simple => simple.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => null,
        };
    }

    private static bool IsFromAttribute(string name)
    {
        foreach (var candidate in FromAttributeNames)
        {
            if (string.Equals(name, candidate, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool TryGetStrategy(string attributeClassName, out ResolveStrategyModel strategy)
    {
        switch (attributeClassName)
        {
            case "FromAncestorAttribute":
                strategy = ResolveStrategyModel.Ancestor;
                return true;
            case "FromOwnerAttribute":
                strategy = ResolveStrategyModel.Owner;
                return true;
            case "FromGroupAttribute":
                strategy = ResolveStrategyModel.Group;
                return true;
            case "FromChildAttribute":
                strategy = ResolveStrategyModel.Child;
                return true;
            default:
                strategy = default;
                return false;
        }
    }

    private static bool InheritsFromGodotNode(INamedTypeSymbol? type)
    {
        while (type is not null)
        {
            if (type.ContainingNamespace?.ToDisplayString() == "Godot" &&
                type.Name == "Node")
                return true;

            type = type.BaseType;
        }
        return false;
    }

    private static bool IsCollectionType(ITypeSymbol type, out ITypeSymbol? elementType)
    {
        elementType = null;

        if (type is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.TypeArguments.Length == 1)
        {
            var originalDef = namedType.OriginalDefinition;
            var ns = originalDef.ContainingNamespace?.ToDisplayString();

            if (ns == "System.Collections.Generic" &&
                (originalDef.Name == "IEnumerable" || originalDef.Name == "IReadOnlyList"))
            {
                elementType = namedType.TypeArguments[0];
                return true;
            }
        }

        return false;
    }

    private static bool? GetNamedArgBool(AttributeData attr, string name)
    {
        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Key == name && namedArg.Value.Value is bool boolValue)
                return boolValue;
        }
        return null;
    }

    private static string? GetConstructorArgString(AttributeData attr, int index)
    {
        if (attr.ConstructorArguments.Length > index &&
            attr.ConstructorArguments[index].Value is string strValue)
            return strValue;

        return null;
    }

    private static string AccessibilityToString(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal",
        };
    }

    /// <summary>
    /// Intermediate data carrier for a single property between extraction and grouping.
    /// </summary>
    internal sealed class PropertyInfo
    {
        public string ClassName { get; }
        public string? Namespace { get; }
        public string Accessibility { get; }
        public PropertyModel Property { get; }
        public List<Diagnostic>? Diagnostics { get; }
        public bool HasErrors { get; }

        public PropertyInfo(
            string className,
            string? ns,
            string accessibility,
            PropertyModel property,
            List<Diagnostic>? diagnostics,
            bool hasErrors)
        {
            ClassName = className;
            Namespace = ns;
            Accessibility = accessibility;
            Property = property;
            Diagnostics = diagnostics;
            HasErrors = hasErrors;
        }
    }
}
