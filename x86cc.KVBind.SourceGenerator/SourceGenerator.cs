using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace x86cc.KVBind.SourceGenerator;

[Generator]
public sealed class SourceGenerator : IIncrementalGenerator
{
    private const string KvBindAttributeFqn       = "x86cc.KVBind.Core.KVBindAttribute";
    private const string KvCoreNamespace          = "x86cc.KVBind.Core";
    private const string KvNodeTypeName           = "KVNode";
    private const string KvNestedNodeTypeName     = "KVNestedNode";
    private const string KvCollectionNodeTypeName = "KVCollectionNode";

    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    // ── Diagnostics ────────────────────────────────────────────────────────────

    private static readonly DiagnosticDescriptor PropertyMustBePartial = new(
        id: "KVB001",
        title: "KVBind property must be partial",
        messageFormat: "Property '{0}' must be declared as partial to be KV-bound",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: null,
        helpLinkUri: "https://github.com/x86cc/KVBind/docs/KVB001");

    private static readonly DiagnosticDescriptor CanonicalKeyRequired = new(
        id: "KVB002",
        title: "Canonical key is required",
        messageFormat: "Property '{0}' must declare a non-empty canonical key in [KVBind(...)]",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: null,
        helpLinkUri: "https://github.com/x86cc/KVBind/docs/KVB002");

    private static readonly DiagnosticDescriptor DuplicateCanonicalKey = new(
        id: "KVB003",
        title: "Duplicate canonical key",
        messageFormat: "Type '{0}' contains duplicate canonical key '{1}'",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: null,
        helpLinkUri: "https://github.com/x86cc/KVBind/docs/KVB003");

    private static readonly DiagnosticDescriptor InvalidCanonicalKey = new(
        id: "KVB004",
        title: "Invalid canonical key",
        messageFormat: "Property '{0}' has invalid canonical key '{1}'. KVBind keys may only contain A-Z, a-z, 0-9, and underscore.",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: null,
        helpLinkUri: "https://github.com/x86cc/KVBind/docs/KVB004");

    private static readonly DiagnosticDescriptor KVBindOnNonKvNodeClass = new(
        id: "KVB005",
        title: "KVBind attribute on non-KVNode class",
        messageFormat: "Property '{0}' has [KVBind] but its containing type '{1}' does not inherit from KVNode. The attribute will be ignored.",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: null,
        helpLinkUri: "https://github.com/x86cc/KVBind/docs/KVB005");

    private static readonly DiagnosticDescriptor MutableNavigationProperty = new(
        id: "KVB006",
        title: "Field group and collection properties must not have a setter",
        messageFormat: "Property '{0}' (type '{1}') must be read-only. Field group and collection instances are bound by the framework and cannot be replaced. Declare as 'public {1} {0} {{ get; }} = new();'.",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: null,
        helpLinkUri: "https://github.com/x86cc/KVBind/docs/KVB006");

    private static readonly DiagnosticDescriptor PartialNavigationProperty = new(
        id: "KVB007",
        title: "Field group and collection properties must not use partial",
        messageFormat: "Property '{0}' (type '{1}') must not be declared partial. The framework binds the existing instance automatically. Declare as 'public {1} {0} {{ get; }} = new();'.",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: null,
        helpLinkUri: "https://github.com/x86cc/KVBind/docs/KVB007");

    private static readonly DiagnosticDescriptor PublicNestedNodeSetter = new(
        id: "KVB008",
        title: "Nested node property setter should not be public",
        messageFormat: "Property '{0}' is a nested node with a public setter. Structural node replacement should go through patch operations (INIT/DROP). Declare as 'public partial {1} {0} {{ get; private set; }}'.",
        category: "KVBind.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: null,
        helpLinkUri: "https://github.com/x86cc/KVBind/docs/KVB008");

    // ── Initialization ─────────────────────────────────────────────────────────

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Main pipeline: one output per KVNode model — per-model incrementality.
        var models = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax t
                    && t.Modifiers.Any(static m => m.Text == "partial"),
                static (ctx, ct) => BuildTypeModel(ctx, ct))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!.Value);

        context.RegisterSourceOutput(models, static (spc, model) => EmitType(spc, model));

        // KVB005 pipeline: warn when [KVBind] appears on a non-KVNode class.
        var nonNodeWarnings = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax t
                    && t.Modifiers.Any(static m => m.Text == "partial"),
                static (ctx, ct) => BuildNonKvNodeWarning(ctx, ct))
            .Where(static w => w is not null)
            .Select(static (w, _) => w!.Value);

        context.RegisterSourceOutput(nonNodeWarnings, static (spc, w) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                KVBindOnNonKvNodeClass, w.Location.Value, w.PropertyName, w.TypeName)));
    }

    // ── Model building ─────────────────────────────────────────────────────────

    private static TypeModel? BuildTypeModel(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.Node is not TypeDeclarationSyntax typeDeclaration)
            return null;

        if (context.SemanticModel.GetDeclaredSymbol(typeDeclaration, ct) is not INamedTypeSymbol typeSymbol)
            return null;

        if (!IsKvNodeType(typeSymbol))
            return null;

        var properties = new List<PropertyModel>();
        foreach (var member in typeDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();

            if (context.SemanticModel.GetDeclaredSymbol(member, ct) is not IPropertySymbol propertySymbol)
                continue;

            var attributeData = propertySymbol.GetAttributes()
                .FirstOrDefault(static a => a.AttributeClass?.ToDisplayString() == KvBindAttributeFqn);

            if (attributeData is null)
                continue;

            var canonicalKey = attributeData.ConstructorArguments.Length > 0
                ? attributeData.ConstructorArguments[0].Value as string
                : null;

            var hasPartialModifier = member.Modifiers.Any(static m => m.Text == "partial");
            var isNestedNode = IsKvNestedNodeType(propertySymbol.Type);
            var setter = propertySymbol.SetMethod;
            var isInitOnly = setter?.IsInitOnly == true;

            properties.Add(new PropertyModel(
                PropertyName: propertySymbol.Name,
                PropertyTypeName: propertySymbol.Type.ToDisplayString(FullyQualifiedNullableFormat),
                NonNullablePropertyTypeName: propertySymbol.Type
                    .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                    .ToDisplayString(FullyQualifiedNullableFormat),
                CanonicalKey: canonicalKey,
                IsNodeProperty: !isNestedNode && IsKvBindNode(propertySymbol.Type),
                IsNestedNodeProperty: isNestedNode,
                IsCollectionProperty: IsKvCollection(propertySymbol.Type),
                HasPartialModifier: hasPartialModifier,
                HasSetter: setter is not null && !isInitOnly,
                IsInitOnlySetter: isInitOnly,
                SetterAccessibility: GetSetterAccessibility(setter),
                Location: new EquatableLocation(
                    propertySymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().GetLocation()
                    ?? member.GetLocation())));
        }

        if (properties.Count == 0)
            return null;

        return new TypeModel(
            TypeName: typeSymbol.Name,
            FullyQualifiedTypeName: typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            NamespaceName: typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : typeSymbol.ContainingNamespace.ToDisplayString(),
            Properties: new EquatableArray<PropertyModel>(properties.ToImmutableArray()));
    }

    private static NonKvNodeWarning? BuildNonKvNodeWarning(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.Node is not TypeDeclarationSyntax typeDeclaration)
            return null;

        if (context.SemanticModel.GetDeclaredSymbol(typeDeclaration, ct) is not INamedTypeSymbol typeSymbol)
            return null;

        if (IsKvNodeType(typeSymbol))
            return null;

        foreach (var member in typeDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();

            if (context.SemanticModel.GetDeclaredSymbol(member, ct) is not IPropertySymbol propertySymbol)
                continue;

            var hasKvBind = propertySymbol.GetAttributes()
                .Any(static a => a.AttributeClass?.ToDisplayString() == KvBindAttributeFqn);

            if (!hasKvBind)
                continue;

            return new NonKvNodeWarning(
                PropertyName: propertySymbol.Name,
                TypeName: typeSymbol.Name,
                Location: new EquatableLocation(member.GetLocation()));
        }

        return null;
    }

    // ── Emission ───────────────────────────────────────────────────────────────

    private static void EmitType(SourceProductionContext context, TypeModel model)
    {
        // Validate and report diagnostics
        foreach (var property in model.Properties.AsImmutableArray())
        {
            if ((!property.IsNodeProperty && !property.IsCollectionProperty && !property.HasPartialModifier)
                || (property.IsNestedNodeProperty && !property.HasPartialModifier))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PropertyMustBePartial, property.Location.Value, property.PropertyName));
            }

            if (string.IsNullOrWhiteSpace(property.CanonicalKey))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    CanonicalKeyRequired, property.Location.Value, property.PropertyName));
            }
            else if (!IsValidCanonicalKey(property.CanonicalKey))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidCanonicalKey, property.Location.Value, property.PropertyName, property.CanonicalKey));
            }

            // KVB006: field group or collection with a setter — unbound replacement is a silent runtime failure
            if ((property.IsNodeProperty || property.IsCollectionProperty) && property.HasSetter)
                context.ReportDiagnostic(Diagnostic.Create(
                    MutableNavigationProperty, property.Location.Value, property.PropertyName, property.PropertyTypeName));

            // KVB007: field group or collection declared as partial — no generated implementation exists
            if ((property.IsNodeProperty || property.IsCollectionProperty) && property.HasPartialModifier)
                context.ReportDiagnostic(Diagnostic.Create(
                    PartialNavigationProperty, property.Location.Value, property.PropertyName, property.PropertyTypeName));

            // KVB008: nested node with a public setter — should use private set to route through INIT/DROP
            if (property.IsNestedNodeProperty && property.HasSetter
                && string.IsNullOrEmpty(property.SetterAccessibility))
                context.ReportDiagnostic(Diagnostic.Create(
                    PublicNestedNodeSetter, property.Location.Value, property.PropertyName, property.NonNullablePropertyTypeName));
        }

        // KVB003: report on every member of each duplicate group, not just the first
        var duplicateGroups = model.Properties.AsImmutableArray()
            .Where(static p => !string.IsNullOrWhiteSpace(p.CanonicalKey) && IsValidCanonicalKey(p.CanonicalKey))
            .GroupBy(static p => p.CanonicalKey!, StringComparer.Ordinal)
            .Where(static g => g.Count() > 1);

        foreach (var group in duplicateGroups)
            foreach (var duplicate in group)
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateCanonicalKey, duplicate.Location.Value, model.TypeName, group.Key));

        // Emit valid field and nested-node properties
        var validFields = model.Properties.AsImmutableArray()
            .Where(static p => !p.IsNodeProperty && !p.IsNestedNodeProperty && !p.IsCollectionProperty
                && p.HasPartialModifier
                && !string.IsNullOrWhiteSpace(p.CanonicalKey) && IsValidCanonicalKey(p.CanonicalKey))
            .ToArray();

        var validNestedNodes = model.Properties.AsImmutableArray()
            .Where(static p => p.IsNestedNodeProperty && p.HasPartialModifier
                && !string.IsNullOrWhiteSpace(p.CanonicalKey) && IsValidCanonicalKey(p.CanonicalKey))
            .ToArray();

        if (validFields.Length == 0 && validNestedNodes.Length == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        if (!string.IsNullOrWhiteSpace(model.NamespaceName))
        {
            sb.AppendLine($"namespace {model.NamespaceName};");
            sb.AppendLine();
        }

        sb.AppendLine($"public partial class {model.TypeName}");
        sb.AppendLine("{");

        foreach (var prop in validFields)
        {
            var key = Escape(prop.CanonicalKey!);
            sb.AppendLine($"    public partial {prop.PropertyTypeName} {prop.PropertyName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        get => GetField<{prop.PropertyTypeName}>(\"{key}\");");
            if (prop.HasSetter)
                sb.AppendLine($"        set => SetField(\"{key}\", value);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        foreach (var prop in validNestedNodes)
        {
            var key = Escape(prop.CanonicalKey!);
            sb.AppendLine($"    public partial {prop.PropertyTypeName} {prop.PropertyName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        get => GetNestedNode<{prop.NonNullablePropertyTypeName}>(\"{key}\");");
            if (prop.HasSetter)
                sb.AppendLine($"        {prop.SetterAccessibility}set => SetNestedNode(\"{key}\", value);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        // Unique hint name using fully qualified type name to avoid collisions
        // e.g. global::Demo.Claims.InsuranceClaim → Demo_Claims_InsuranceClaim.KVBind.g.cs
        var hintName = model.FullyQualifiedTypeName
            .Replace("global::", string.Empty)
            .Replace('.', '_')
            .Replace('<', '[')
            .Replace('>', ']')
            + ".KVBind.g.cs";

        context.AddSource(hintName, sb.ToString());
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static bool IsValidCanonicalKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var c in value!)
        {
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
                continue;
            return false;
        }

        return true;
    }

    private static bool IsKvBindNode(ITypeSymbol typeSymbol) =>
        typeSymbol is INamedTypeSymbol named && IsKvNodeType(named);

    private static bool IsKvCollection(ITypeSymbol typeSymbol) =>
        typeSymbol is INamedTypeSymbol named
        && named.IsGenericType
        && named.ConstructedFrom.Name == KvCollectionNodeTypeName
        && named.ConstructedFrom.ContainingNamespace.ToDisplayString() == KvCoreNamespace;

    private static bool IsKvNodeType(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol.BaseType;
        while (current is not null)
        {
            if (current.Name == KvNodeTypeName
                && current.ContainingNamespace.ToDisplayString() == KvCoreNamespace)
                return true;
            current = current.BaseType;
        }

        return typeSymbol.Name == KvNodeTypeName
            && typeSymbol.ContainingNamespace.ToDisplayString() == KvCoreNamespace;
    }

    private static bool IsKvNestedNodeType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol named)
            return false;

        var current = (INamedTypeSymbol?)named;
        while (current is not null)
        {
            if (current.Name == KvNestedNodeTypeName
                && current.ContainingNamespace.ToDisplayString() == KvCoreNamespace)
                return true;
            current = current.BaseType;
        }

        return false;
    }

    private static string? GetSetterAccessibility(IMethodSymbol? setter) =>
        setter?.DeclaredAccessibility switch
        {
            Accessibility.Private => "private ",
            Accessibility.Protected => "protected ",
            Accessibility.Internal => "internal ",
            Accessibility.ProtectedAndInternal => "private protected ",
            Accessibility.ProtectedOrInternal => "protected internal ",
            _ => string.Empty
        };

    // ── Data models (value-equatable for incremental pipeline caching) ─────────

    // Wraps Roslyn's Location (which has no IEquatable) for use in record structs.
    private readonly struct EquatableLocation(Location location) : IEquatable<EquatableLocation>
    {
        public Location Value => location;

        public bool Equals(EquatableLocation other)
        {
            var a = location.GetLineSpan();
            var b = other.Value.GetLineSpan();
            return a.Path == b.Path
                && a.Span.Start == b.Span.Start
                && a.Span.End == b.Span.End;
        }

        public override bool Equals(object? obj) => obj is EquatableLocation e && Equals(e);

        public override int GetHashCode()
        {
            var span = location.GetLineSpan();
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (span.Path?.GetHashCode() ?? 0);
                hash = hash * 31 + span.Span.Start.Line;
                hash = hash * 31 + span.Span.Start.Character;
                return hash;
            }
        }
    }

    // Wraps ImmutableArray<T> to provide structural equality for incremental caching.
    private readonly struct EquatableArray<T>(ImmutableArray<T> array) : IEquatable<EquatableArray<T>>
        where T : IEquatable<T>
    {
        private readonly ImmutableArray<T> _array = array.IsDefault ? ImmutableArray<T>.Empty : array;

        public ImmutableArray<T> AsImmutableArray() => _array;

        public bool Equals(EquatableArray<T> other)
        {
            if (_array.Length != other._array.Length)
                return false;
            for (var i = 0; i < _array.Length; i++)
                if (!_array[i].Equals(other._array[i]))
                    return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is EquatableArray<T> e && Equals(e);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                foreach (var item in _array)
                    hash = hash * 31 + item.GetHashCode();
                return hash;
            }
        }
    }

    private readonly record struct TypeModel(
        string TypeName,
        string FullyQualifiedTypeName,
        string? NamespaceName,
        EquatableArray<PropertyModel> Properties);

    private readonly record struct PropertyModel(
        string PropertyName,
        string PropertyTypeName,
        string NonNullablePropertyTypeName,
        string? CanonicalKey,
        bool IsNodeProperty,
        bool IsNestedNodeProperty,
        bool IsCollectionProperty,
        bool HasPartialModifier,
        bool HasSetter,
        bool IsInitOnlySetter,
        string? SetterAccessibility,
        EquatableLocation Location);

    private readonly record struct NonKvNodeWarning(
        string PropertyName,
        string TypeName,
        EquatableLocation Location);
}
