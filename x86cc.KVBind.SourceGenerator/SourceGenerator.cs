using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace x86cc.KVBind.SourceGenerator;

[Generator]
public sealed class SourceGenerator : IIncrementalGenerator
{
    private const string KVBindAttributeTypeName = "x86cc.KVBind.Core.KVBindAttribute";
    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor PropertyMustBePartial = new(
        id: "KVB001",
        title: "KVBind property must be partial",
        messageFormat: "Property '{0}' must be declared as partial to be KV-bound.",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CanonicalKeyRequired = new(
        id: "KVB002",
        title: "Canonical key is required",
        messageFormat: "Property '{0}' must declare a non-empty canonical key in [KVBind(...)]",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateCanonicalKey = new(
        id: "KVB003",
        title: "Duplicate canonical key",
        messageFormat: "Type '{0}' contains duplicate canonical key '{1}'.",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidCanonicalKey = new(
        id: "KVB004",
        title: "Invalid canonical key",
        messageFormat: "Property '{0}' has invalid canonical key '{1}'. KVBind keys may only contain A-Z, a-z, 0-9, and underscore.",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax typeDeclaration && typeDeclaration.Modifiers.Any(static m => m.Text == "partial"),
                static (ctx, _) => BuildTypeModel(ctx))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .Collect();

        context.RegisterSourceOutput(models, static (spc, source) => Emit(spc, source));
    }

    private static TypeModel? BuildTypeModel(GeneratorSyntaxContext context)
    {
        if (context.Node is not TypeDeclarationSyntax typeDeclaration)
        {
            return null;
        }

        if (context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        if (!IsKvNodeType(typeSymbol))
        {
            return null;
        }

        var properties = new List<PropertyModel>();
        foreach (var member in typeDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (context.SemanticModel.GetDeclaredSymbol(member) is not IPropertySymbol propertySymbol)
            {
                continue;
            }

            var attributeData = propertySymbol
                .GetAttributes()
                .FirstOrDefault(static data => data.AttributeClass?.ToDisplayString() == KVBindAttributeTypeName);

            if (attributeData is null)
            {
                continue;
            }

            var canonicalKey = attributeData.ConstructorArguments.Length > 0
                ? attributeData.ConstructorArguments[0].Value as string
                : null;

            var hasPartialModifier = member.Modifiers.Any(static modifier => modifier.Text == "partial");

            var isNestedNodeProperty = IsKvNestedNodeType(propertySymbol.Type);
            properties.Add(new PropertyModel(
                propertySymbol.Name,
                propertySymbol.Type.ToDisplayString(FullyQualifiedNullableFormat),
                propertySymbol.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(FullyQualifiedNullableFormat),
                canonicalKey,
                !isNestedNodeProperty && IsKvBindNode(propertySymbol.Type),
                isNestedNodeProperty,
                IsKvCollection(propertySymbol.Type),
                hasPartialModifier,
                propertySymbol.SetMethod is not null,
                GetSetterAccessibility(propertySymbol.SetMethod),
                propertySymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation() ?? member.GetLocation()));
        }

        return properties.Count == 0
            ? null
            : new TypeModel(
                typeSymbol.Name,
                typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                typeSymbol.ContainingNamespace.IsGlobalNamespace ? null : typeSymbol.ContainingNamespace.ToDisplayString(),
                properties);
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<TypeModel> models)
    {
        foreach (var model in models)
        {
            EmitType(context, model);
        }
    }

    private static void EmitType(SourceProductionContext context, TypeModel model)
    {
        foreach (var property in model.Properties)
        {
            if ((!property.IsNodeProperty && !property.IsCollectionProperty && !property.HasPartialModifier)
                || (property.IsNestedNodeProperty && !property.HasPartialModifier))
            {
                context.ReportDiagnostic(Diagnostic.Create(PropertyMustBePartial, property.Location, property.PropertyName));
            }

            if (string.IsNullOrWhiteSpace(property.CanonicalKey))
            {
                context.ReportDiagnostic(Diagnostic.Create(CanonicalKeyRequired, property.Location, property.PropertyName));
            }
            else if (!IsValidCanonicalKey(property.CanonicalKey))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidCanonicalKey, property.Location, property.PropertyName, property.CanonicalKey));
            }
        }

        foreach (var group in model.Properties
                     .Where(static property => !string.IsNullOrWhiteSpace(property.CanonicalKey) && IsValidCanonicalKey(property.CanonicalKey))
                     .GroupBy(static property => property.CanonicalKey!, StringComparer.Ordinal)
                     .Where(static g => g.Count() > 1))
        {
            context.ReportDiagnostic(Diagnostic.Create(DuplicateCanonicalKey, group.First().Location, model.TypeName, group.Key));
        }

        var validProperties = model.Properties
            .Where(static property => !property.IsNodeProperty && !property.IsNestedNodeProperty && !property.IsCollectionProperty && property.HasPartialModifier && !string.IsNullOrWhiteSpace(property.CanonicalKey) && IsValidCanonicalKey(property.CanonicalKey))
            .ToArray();

        var validNestedNodeProperties = model.Properties
            .Where(static property => property.IsNestedNodeProperty && property.HasPartialModifier && !string.IsNullOrWhiteSpace(property.CanonicalKey) && IsValidCanonicalKey(property.CanonicalKey))
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        if (!string.IsNullOrWhiteSpace(model.NamespaceName))
        {
            builder.AppendLine($"namespace {model.NamespaceName};");
            builder.AppendLine();
        }

        builder.AppendLine($"public partial class {model.TypeName}");
        builder.AppendLine("{");
        foreach (var property in validProperties)
        {
            var key = Escape(property.CanonicalKey!);
            builder.AppendLine($"    public partial {property.PropertyTypeName} {property.PropertyName}");
            builder.AppendLine("    {");
            builder.AppendLine($"        get => GetField<{property.PropertyTypeName}>(\"{key}\");");
            if (property.HasSetter)
            {
                builder.AppendLine($"        set => SetField(\"{key}\", value);");
            }

            builder.AppendLine("    }");
            builder.AppendLine();
        }

        foreach (var property in validNestedNodeProperties)
        {
            var key = Escape(property.CanonicalKey!);
            builder.AppendLine($"    public partial {property.PropertyTypeName} {property.PropertyName}");
            builder.AppendLine("    {");
            builder.AppendLine($"        get => GetNestedNode<{property.NonNullablePropertyTypeName}>(\"{key}\");");
            if (property.HasSetter)
            {
                builder.AppendLine($"        {property.SetterAccessibility}set => SetNestedNode(\"{key}\", value);");
            }

            builder.AppendLine("    }");
            builder.AppendLine();
        }

        builder.AppendLine("}");
        context.AddSource($"{model.TypeName}.KVBind.g.cs", builder.ToString());
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static bool IsValidCanonicalKey(string? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if ((character >= 'A' && character <= 'Z')
                || (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsKvBindNode(ITypeSymbol typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType
            && IsKvNodeType(namedType);
    }

    private static bool IsKvCollection(ITypeSymbol typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType
            && namedType.IsGenericType
            && (namedType.ConstructedFrom.Name == "KVCollectionNode2"
                || namedType.ConstructedFrom.Name == "KVCollectionNode"
                || namedType.ConstructedFrom.Name == "KVCollection")
            && namedType.ConstructedFrom.ContainingNamespace.ToDisplayString() == "x86cc.KVBind.Core";
    }
    
    private static bool IsKvNodeType(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol;
        while (current is not null)
        {
            if (current.Name == "KVNode" && current.ContainingNamespace.ToDisplayString() == "x86cc.KVBind.Core")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool IsKvNestedNodeType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var current = namedType;
        while (current is not null)
        {
            if (current.Name == "KVNestedNode" && current.ContainingNamespace.ToDisplayString() == "x86cc.KVBind.Core")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static string? GetSetterAccessibility(IMethodSymbol? setter)
    {
        if (setter is null)
        {
            return null;
        }

        return setter.DeclaredAccessibility switch
        {
            Accessibility.Private => "private ",
            Accessibility.Protected => "protected ",
            Accessibility.Internal => "internal ",
            Accessibility.ProtectedAndInternal => "private protected ",
            Accessibility.ProtectedOrInternal => "protected internal ",
            _ => string.Empty
        };
    }

    private sealed class TypeModel(
        string typeName,
        string fullyQualifiedTypeName,
        string? namespaceName,
        IReadOnlyList<PropertyModel> properties)
    {
        public string TypeName { get; } = typeName;

        public string FullyQualifiedTypeName { get; } = fullyQualifiedTypeName;

        public string? NamespaceName { get; } = namespaceName;

        public IReadOnlyList<PropertyModel> Properties { get; } = properties;
    }

    private sealed class PropertyModel(
        string propertyName,
        string propertyTypeName,
        string nonNullablePropertyTypeName,
        string? canonicalKey,
        bool isNodeProperty,
        bool isNestedNodeProperty,
        bool isCollectionProperty,
        bool hasPartialModifier,
        bool hasSetter,
        string? setterAccessibility,
        Location location)
    {
        public string PropertyName { get; } = propertyName;

        public string PropertyTypeName { get; } = propertyTypeName;

        public string NonNullablePropertyTypeName { get; } = nonNullablePropertyTypeName;

        public string? CanonicalKey { get; } = canonicalKey;

        public bool IsNodeProperty { get; } = isNodeProperty;

        public bool IsNestedNodeProperty { get; } = isNestedNodeProperty;

        public bool IsCollectionProperty { get; } = isCollectionProperty;

        public bool HasPartialModifier { get; } = hasPartialModifier;

        public bool HasSetter { get; } = hasSetter;

        public string? SetterAccessibility { get; } = setterAccessibility;

        public Location Location { get; } = location;
    }
}
