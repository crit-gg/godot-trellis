using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GodotTrellis.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class TrellisSourceGenerator : IIncrementalGenerator
{
    private const string FromAncestorAttributeMetadataName = "GodotTrellis.FromAncestorAttribute";
    private const string FromOwnerAttributeMetadataName = "GodotTrellis.FromOwnerAttribute";
    private const string FromGroupAttributeMetadataName = "GodotTrellis.FromGroupAttribute";
    private const string FromChildAttributeMetadataName = "GodotTrellis.FromChildAttribute";
    private const string FromSiblingAttributeMetadataName = "GodotTrellis.FromSiblingAttribute";
    private const string GodotNodeMetadataName = "Godot.Node";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var fromAncestorProperties = CreateAttributedPropertyProvider(
            context, FromAncestorAttributeMetadataName);
        var fromOwnerProperties = CreateAttributedPropertyProvider(
            context, FromOwnerAttributeMetadataName);
        var fromGroupProperties = CreateAttributedPropertyProvider(
            context, FromGroupAttributeMetadataName);
        var fromChildProperties = CreateAttributedPropertyProvider(
            context, FromChildAttributeMetadataName);
        var fromSiblingProperties = CreateAttributedPropertyProvider(
            context, FromSiblingAttributeMetadataName);

        var candidateProperties = fromAncestorProperties
            .Collect()
            .Combine(fromOwnerProperties.Collect())
            .Combine(fromGroupProperties.Collect())
            .Combine(fromChildProperties.Collect())
            .Combine(fromSiblingProperties.Collect())
            .Select(static (x, _) => MergePropertySymbols(
                x.Left.Left.Left.Left,
                x.Left.Left.Left.Right,
                x.Left.Left.Right,
                x.Left.Right,
                x.Right));

        var symbols = context.CompilationProvider
            .Select(static (compilation, _) => SymbolLookup.Create(compilation));

        var propertyInfos = candidateProperties
            .Combine(symbols)
            .Select(static (x, ct) => BuildPropertyInfos(
                x.Left,
                x.Right,
                ct));

        context.RegisterSourceOutput(propertyInfos, static (spc, properties) =>
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

    private static IncrementalValuesProvider<IPropertySymbol> CreateAttributedPropertyProvider(
        IncrementalGeneratorInitializationContext context,
        string attributeMetadataName)
    {
        return context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: attributeMetadataName,
            predicate: static (node, _) => node is PropertyDeclarationSyntax,
            transform: static (ctx, _) => (IPropertySymbol)ctx.TargetSymbol);
    }

    private static ImmutableArray<IPropertySymbol> MergePropertySymbols(
        ImmutableArray<IPropertySymbol> fromAncestorProperties,
        ImmutableArray<IPropertySymbol> fromOwnerProperties,
        ImmutableArray<IPropertySymbol> fromGroupProperties,
        ImmutableArray<IPropertySymbol> fromChildProperties,
        ImmutableArray<IPropertySymbol> fromSiblingProperties)
    {
        var builder = ImmutableArray.CreateBuilder<IPropertySymbol>(
            fromAncestorProperties.Length +
            fromOwnerProperties.Length +
            fromGroupProperties.Length +
            fromChildProperties.Length +
            fromSiblingProperties.Length);

        builder.AddRange(fromAncestorProperties);
        builder.AddRange(fromOwnerProperties);
        builder.AddRange(fromGroupProperties);
        builder.AddRange(fromChildProperties);
        builder.AddRange(fromSiblingProperties);

        return builder.ToImmutable();
    }

    private static ImmutableArray<PropertyInfo> BuildPropertyInfos(
        ImmutableArray<IPropertySymbol> propertySymbols,
        SymbolLookup symbols,
        CancellationToken ct)
    {
        var seen = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);
        var builder = ImmutableArray.CreateBuilder<PropertyInfo>();

        foreach (var propSymbol in propertySymbols)
        {
            ct.ThrowIfCancellationRequested();

            if (!seen.Add(propSymbol))
                continue;

            var info = ExtractPropertyInfo(propSymbol, symbols, ct);
            if (info is not null)
                builder.Add(info);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Semantic analysis of a candidate property.
    /// </summary>
    private static PropertyInfo? ExtractPropertyInfo(
        IPropertySymbol propSymbol,
        SymbolLookup symbols,
        CancellationToken ct)
    {
        var declarations = GetPropertyDeclarations(propSymbol, ct);
        var location = declarations.Count > 0
            ? declarations[0].GetLocation()
            : propSymbol.Locations.FirstOrDefault();

        if (location is null)
            return null;

        // Find From* attributes.
        var fromAttributes = new List<(AttributeData attr, ResolveStrategyModel strategy)>();

        foreach (var attrData in propSymbol.GetAttributes())
        {
            if (TryGetStrategy(attrData.AttributeClass, symbols, out var strategy))
                fromAttributes.Add((attrData, strategy));
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
                location,
                propSymbol.Name));
            hasErrors = true;
        }

        var (primaryAttr, primaryStrategy) = fromAttributes[0];

        // GTR001: Property must be partial.
        if (!IsPartialProperty(declarations))
        {
            diagnostics ??= new List<Diagnostic>();
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.NonPartialProperty,
                location,
                propSymbol.Name));
            hasErrors = true;
        }

        // GTR002: Containing class must inherit from Godot.Node.
        var containingType = propSymbol.ContainingType;
        if (!InheritsFromGodotNode(containingType, symbols.GodotNode))
        {
            diagnostics ??= new List<Diagnostic>();
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.NotANodeClass,
                location,
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
                location,
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
                location,
                propSymbol.Name,
                propertyType.ToDisplayString()));
            hasErrors = true;
        }

        // Detect IEnumerable<T> / IReadOnlyList<T>.
        string? collectionElementTypeName = null;
        var typeName = propertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var optionalResolveTypeName = isOptional
            ? propertyType.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : null;

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
            optionalResolveTypeName: optionalResolveTypeName,
            strategy: primaryStrategy,
            groupName: groupName,
            deep: deep,
            useSceneFilePath: useSceneFilePath,
            accessModifier: propertyAccessibility);

        return new PropertyInfo(
            className, ns, accessibility, propModel, diagnostics, hasErrors);
    }

    private static List<PropertyDeclarationSyntax> GetPropertyDeclarations(
        IPropertySymbol propertySymbol,
        CancellationToken ct)
    {
        var declarations = new List<PropertyDeclarationSyntax>(
            propertySymbol.DeclaringSyntaxReferences.Length);

        foreach (var syntaxRef in propertySymbol.DeclaringSyntaxReferences)
        {
            ct.ThrowIfCancellationRequested();

            if (syntaxRef.GetSyntax(ct) is PropertyDeclarationSyntax declaration)
                declarations.Add(declaration);
        }

        return declarations;
    }

    private static bool IsPartialProperty(List<PropertyDeclarationSyntax> declarations)
    {
        foreach (var declaration in declarations)
        {
            if (declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return true;
        }

        return false;
    }

    private static bool TryGetStrategy(
        INamedTypeSymbol? attributeClass,
        SymbolLookup symbols,
        out ResolveStrategyModel strategy)
    {
        if (attributeClass is null)
        {
            strategy = default;
            return false;
        }

        if (symbols.FromAncestorAttribute is not null &&
            SymbolEqualityComparer.Default.Equals(attributeClass, symbols.FromAncestorAttribute))
        {
            strategy = ResolveStrategyModel.Ancestor;
            return true;
        }

        if (symbols.FromOwnerAttribute is not null &&
            SymbolEqualityComparer.Default.Equals(attributeClass, symbols.FromOwnerAttribute))
        {
            strategy = ResolveStrategyModel.Owner;
            return true;
        }

        if (symbols.FromGroupAttribute is not null &&
            SymbolEqualityComparer.Default.Equals(attributeClass, symbols.FromGroupAttribute))
        {
            strategy = ResolveStrategyModel.Group;
            return true;
        }

        if (symbols.FromChildAttribute is not null &&
            SymbolEqualityComparer.Default.Equals(attributeClass, symbols.FromChildAttribute))
        {
            strategy = ResolveStrategyModel.Child;
            return true;
        }

        if (symbols.FromSiblingAttribute is not null &&
            SymbolEqualityComparer.Default.Equals(attributeClass, symbols.FromSiblingAttribute))
        {
            strategy = ResolveStrategyModel.Sibling;
            return true;
        }

        strategy = default;
        return false;
    }

    private static bool InheritsFromGodotNode(
        INamedTypeSymbol? type,
        INamedTypeSymbol? godotNodeType)
    {
        if (godotNodeType is null)
            return false;

        while (type is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(type, godotNodeType))
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

    private readonly struct SymbolLookup
    {
        public INamedTypeSymbol? FromAncestorAttribute { get; }
        public INamedTypeSymbol? FromOwnerAttribute { get; }
        public INamedTypeSymbol? FromGroupAttribute { get; }
        public INamedTypeSymbol? FromChildAttribute { get; }
        public INamedTypeSymbol? FromSiblingAttribute { get; }
        public INamedTypeSymbol? GodotNode { get; }

        public SymbolLookup(
            INamedTypeSymbol? fromAncestorAttribute,
            INamedTypeSymbol? fromOwnerAttribute,
            INamedTypeSymbol? fromGroupAttribute,
            INamedTypeSymbol? fromChildAttribute,
            INamedTypeSymbol? fromSiblingAttribute,
            INamedTypeSymbol? godotNode)
        {
            FromAncestorAttribute = fromAncestorAttribute;
            FromOwnerAttribute = fromOwnerAttribute;
            FromGroupAttribute = fromGroupAttribute;
            FromChildAttribute = fromChildAttribute;
            FromSiblingAttribute = fromSiblingAttribute;
            GodotNode = godotNode;
        }

        public static SymbolLookup Create(Compilation compilation)
        {
            return new SymbolLookup(
                compilation.GetTypeByMetadataName(FromAncestorAttributeMetadataName),
                compilation.GetTypeByMetadataName(FromOwnerAttributeMetadataName),
                compilation.GetTypeByMetadataName(FromGroupAttributeMetadataName),
                compilation.GetTypeByMetadataName(FromChildAttributeMetadataName),
                compilation.GetTypeByMetadataName(FromSiblingAttributeMetadataName),
                compilation.GetTypeByMetadataName(GodotNodeMetadataName));
        }
    }
}
