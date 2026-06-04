using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using KVBindSourceGenerator = x86cc.KVBind.SourceGenerator.SourceGenerator;

namespace x86cc.KVBind.UnitTests.SourceGenerator;

public class SourceGeneratorSmokeTests
{
    [Fact]
    public void Generator_EmitsPropertyAccessors_ForAttributedPartialProperty()
    {
        var source = """
            using x86cc.KVBind.Core;

            namespace Demo;

            public partial class ContractModel : KVRootNode
            {
                [KVBind("ContractDescription")]
                public partial string ContractDescription { get; set; }
            }
            """;

        var runResult = RunGenerator(source);
        var generatedSources = runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .Select(g => g.SourceText.ToString())
            .ToArray();

        generatedSources.Should().Contain(text => text.Contains("public partial string ContractDescription", StringComparison.Ordinal));
        generatedSources.Should().Contain(text => text.Contains("get => GetField<string>(\"ContractDescription\")", StringComparison.Ordinal));
        generatedSources.Should().Contain(text => text.Contains("set => SetField(\"ContractDescription\", value)", StringComparison.Ordinal));
        generatedSources.Should().NotContain(text => text.Contains("Create(global::x86cc.KVBind.Core.KVModel", StringComparison.Ordinal));
        generatedSources.Should().NotContain(text => text.Contains("node.Bind(model);", StringComparison.Ordinal));
        runResult.Diagnostics.Should().NotContain(d => d.Id == "KVB001" || d.Id == "KVB002" || d.Id == "KVB003" || d.Id == "KVB004");
    }

    [Fact]
    public void Generator_WhenPropertyIsNotPartial_ReportsDiagnostic()
    {
        var source = """
            using x86cc.KVBind.Core;

            namespace Demo;

            public partial class ContractModel : KVRootNode
            {
                [KVBind("ContractDescription")]
                public string ContractDescription { get; set; }
            }
            """;

        var runResult = RunGenerator(source);
        runResult.Diagnostics.Should().Contain(d => d.Id == "KVB001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_WhenCanonicalKeyIsMissing_ReportsDiagnostic()
    {
        var source = """
            using x86cc.KVBind.Core;

            namespace Demo;

            public partial class ContractModel : KVRootNode
            {
                [KVBind("")]
                public partial string ContractDescription { get; set; }
            }
            """;

        var runResult = RunGenerator(source);
        runResult.Diagnostics.Should().Contain(d => d.Id == "KVB002" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_WhenCanonicalKeyUsesAllowedCharacters_DoesNotReportDiagnostic()
    {
        var source = """
            using x86cc.KVBind.Core;

            namespace Demo;

            public partial class ContractModel : KVRootNode
            {
                [KVBind("Valid_ABC123")]
                public partial string ContractDescription { get; set; }
            }
            """;

        var runResult = RunGenerator(source);
        runResult.Diagnostics.Should().NotContain(d => d.Id == "KVB004");
        runResult.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("Invalid/Path")]
    [InlineData("Invalid Path")]
    [InlineData("Invalid-Path")]
    [InlineData("Invalid.Path")]
    public void Generator_WhenCanonicalKeyUsesDisallowedCharacters_ReportsDiagnostic(string canonicalKey)
    {
        var source = $$"""
            using x86cc.KVBind.Core;

            namespace Demo;

            public partial class ContractModel : KVRootNode
            {
                [KVBind("{{canonicalKey}}")]
                public partial string ContractDescription { get; set; }
            }
            """;

        var runResult = RunGenerator(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "KVB004" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_DoesNotRequirePartial_OnKvBindNodeFieldGroupProperty()
    {
        var source = """
            using x86cc.KVBind.Core;

            namespace Demo;

            public partial class ContractModel : KVRootNode
            {
                [KVBind("General")]
                public ContractGeneral General { get; } = new();

                [KVBind("Title")]
                public partial string Title { get; set; }
            }

            public partial class ContractGeneral : KVFieldGroupNode
            {
                [KVBind("Description")]
                public partial string Description { get; set; }
            }
            """;

        var runResult = RunGenerator(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "KVB001");
        runResult.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_DoesNotRequirePartial_OnKvCollectionNodeProperty()
    {
        var source = """
            using x86cc.KVBind.Core;

            namespace Demo;

            public partial class ContractModel : KVRootNode
            {
                [KVBind("Lines")]
                public KVCollectionNode<ContractLine> Lines { get; } = new();

                [KVBind("Title")]
                public partial string Title { get; set; }
            }

            public partial class ContractLine : KVCollectionItemNode
            {
                [KVBind("Name")]
                public partial string Name { get; set; }
            }
            """;

        var runResult = RunGenerator(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "KVB001");
        runResult.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_EmitsNestedNodeAccessors_ForKvNestedNodePartialProperty()
    {
        var source = """
            using x86cc.KVBind.Core;

            namespace Demo;

            public partial class ContractModel : KVRootNode
            {
                [KVBind("Animal")]
                public partial Animal? Animal { get; private set; }
            }

            public abstract partial class Animal : KVNestedNode
            {
            }
            """;

        var runResult = RunGenerator(source);
        var generatedSources = runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .Select(g => g.SourceText.ToString())
            .ToArray();

        generatedSources.Should().Contain(text => text.Contains("public partial global::Demo.Animal? Animal", StringComparison.Ordinal));
        generatedSources.Should().Contain(text => text.Contains("get => GetNestedNode<global::Demo.Animal>(\"Animal\")", StringComparison.Ordinal));
        generatedSources.Should().Contain(text => text.Contains("private set => SetNestedNode(\"Animal\", value)", StringComparison.Ordinal));
        generatedSources.Should().NotContain(text => text.Contains("GetField<global::Demo.Animal", StringComparison.Ordinal));
        runResult.Diagnostics.Should().NotContain(d => d.Id == "KVB001" || d.Id == "KVB002" || d.Id == "KVB003" || d.Id == "KVB004");
    }

    [Fact]
    public void Generator_WhenNestedNodePropertyIsNotPartial_ReportsDiagnostic()
    {
        var source = """
            using x86cc.KVBind.Core;

            namespace Demo;

            public partial class ContractModel : KVRootNode
            {
                [KVBind("Animal")]
                public Animal? Animal { get; private set; }
            }

            public abstract partial class Animal : KVNestedNode
            {
            }
            """;

        var runResult = RunGenerator(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "KVB001" && d.Severity == DiagnosticSeverity.Error);
    }

    // ── New tests covering plan items 4, 5, 7, 9 ──────────────────────────────

    [Fact]
    public void Generator_WhenPropertyHasInitAccessor_EmitsGetterButNoSetter()
    {
        var source = """
            using x86cc.KVBind.Core;
            namespace Demo;
            public partial class ContractModel : KVRootNode
            {
                [KVBind("ContractNumber")]
                public partial string? ContractNumber { get; init; }
            }
            """;

        var runResult = RunGenerator(source);
        var generatedSources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(g => g.SourceText.ToString())
            .ToArray();

        generatedSources.Should().Contain(t => t.Contains("get => GetField<string?>(\"ContractNumber\")", StringComparison.Ordinal));
        generatedSources.Should().NotContain(t => t.Contains("set => SetField", StringComparison.Ordinal));
        runResult.Diagnostics.Should().NotContain(d =>
            d.Id == "KVB001" || d.Id == "KVB002" || d.Id == "KVB003" || d.Id == "KVB004");
    }

    [Fact]
    public void Generator_WhenKVBindOnNonKvNodeClass_ReportsKVB005Warning()
    {
        var source = """
            using x86cc.KVBind.Core;
            namespace Demo;
            public partial class PlainClass
            {
                [KVBind("Title")]
                public partial string? Title { get; set; }
            }
            """;

        var runResult = RunGenerator(source);

        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "KVB005" && d.Severity == DiagnosticSeverity.Warning);
        // KVB005 is a warning, not an error — compilation is not blocked
        runResult.Diagnostics.Should().NotContain(d =>
            d.Id == "KVB005" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_HintName_UsesFullyQualifiedTypeName()
    {
        var source = """
            using x86cc.KVBind.Core;
            namespace Demo.Claims;
            public partial class InsuranceClaim : KVRootNode
            {
                [KVBind("ClaimNumber")]
                public partial string? ClaimNumber { get; set; }
            }
            """;

        var runResult = RunGenerator(source);
        var hintNames = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(g => g.HintName)
            .ToArray();

        hintNames.Should().Contain(h => h.Contains("Demo_Claims_InsuranceClaim", StringComparison.Ordinal));
        hintNames.Should().NotContain(h => h == "InsuranceClaim.KVBind.g.cs");
    }

    [Fact]
    public void Generator_BasicFieldProperty_EmitsCorrectStructure()
    {
        var source = """
            using x86cc.KVBind.Core;
            namespace Demo;
            public partial class ContractModel : KVRootNode
            {
                [KVBind("ContractDescription")]
                public partial string? ContractDescription { get; set; }
            }
            """;

        var runResult = RunGenerator(source);
        var generated = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Single(g => g.HintName.Contains("ContractModel", StringComparison.Ordinal))
            .SourceText.ToString();

        // Header
        generated.Should().Contain("// <auto-generated />");
        generated.Should().Contain("#nullable enable");
        generated.Should().Contain("namespace Demo;");
        // Class declaration
        generated.Should().Contain("public partial class ContractModel");
        // Accessor bodies
        generated.Should().Contain("get => GetField<string?>(\"ContractDescription\");");
        generated.Should().Contain("set => SetField(\"ContractDescription\", value);");
        // No framework-internal calls leaked
        generated.Should().NotContain("BindRuntime");
        generated.Should().NotContain("CreateChildModel");
    }

    [Fact]
    public void Generator_TypeInGlobalNamespace_OmitsNamespaceLine()
    {
        var source = """
            using x86cc.KVBind.Core;
            public partial class GlobalModel : KVRootNode
            {
                [KVBind("Flag")]
                public partial bool Flag { get; set; }
            }
            """;

        var runResult = RunGenerator(source);
        var generated = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Single(g => g.HintName.Contains("GlobalModel", StringComparison.Ordinal))
            .SourceText.ToString();

        generated.Should().NotContain("namespace");
        generated.Should().Contain("get => GetField<bool>(\"Flag\")");
    }

    // ── KVB006 / KVB007 / KVB008 structural navigation diagnostics ────────────

    [Fact]
    public void Generator_WhenFieldGroupHasSetter_ReportsKVB006()
    {
        var source = """
            using x86cc.KVBind.Core;
            namespace Demo;
            public partial class ContractModel : KVRootNode
            {
                [KVBind("Policy")]
                public ClaimPolicy Policy { get; set; } = new();
            }
            public partial class ClaimPolicy : KVFieldGroupNode { }
            """;

        var runResult = RunGenerator(source);
        runResult.Diagnostics.Should().Contain(d => d.Id == "KVB006" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_WhenCollectionHasSetter_ReportsKVB006()
    {
        var source = """
            using x86cc.KVBind.Core;
            namespace Demo;
            public partial class ContractModel : KVRootNode
            {
                [KVBind("Items")]
                public KVCollectionNode<ItemNode> Items { get; set; } = new();
            }
            public partial class ItemNode : KVCollectionItemNode { }
            """;

        var runResult = RunGenerator(source);
        runResult.Diagnostics.Should().Contain(d => d.Id == "KVB006" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_WhenFieldGroupIsPartial_ReportsKVB007()
    {
        var source = """
            using x86cc.KVBind.Core;
            namespace Demo;
            public partial class ContractModel : KVRootNode
            {
                [KVBind("Policy")]
                public partial ClaimPolicy Policy { get; }
            }
            public partial class ClaimPolicy : KVFieldGroupNode { }
            """;

        var runResult = RunGenerator(source);
        runResult.Diagnostics.Should().Contain(d => d.Id == "KVB007" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_WhenNestedNodeHasPublicSetter_ReportsKVB008Warning()
    {
        var source = """
            using x86cc.KVBind.Core;
            namespace Demo;
            public partial class ContractModel : KVRootNode
            {
                [KVBind("Claimant")]
                public partial ClaimantBase? Claimant { get; set; }
            }
            public abstract partial class ClaimantBase : KVNestedNode { }
            """;

        var runResult = RunGenerator(source);
        runResult.Diagnostics.Should().Contain(d => d.Id == "KVB008" && d.Severity == DiagnosticSeverity.Warning);
        // KVB008 is a warning, not an error — the code is still generated
        runResult.Diagnostics.Should().NotContain(d => d.Id == "KVB008" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_WhenStructuralNodesAreCorrectlyDeclared_NoStructuralDiagnostics()
    {
        var source = """
            using x86cc.KVBind.Core;
            namespace Demo;
            public partial class ContractModel : KVRootNode
            {
                [KVBind("Policy")]
                public ClaimPolicy Policy { get; } = new();

                [KVBind("Items")]
                public KVCollectionNode<ItemNode> Items { get; } = new();

                [KVBind("Claimant")]
                public partial ClaimantBase? Claimant { get; private set; }
            }
            public partial class ClaimPolicy : KVFieldGroupNode { }
            public partial class ItemNode : KVCollectionItemNode { }
            public abstract partial class ClaimantBase : KVNestedNode { }
            """;

        var runResult = RunGenerator(source);
        runResult.Diagnostics.Should().NotContain(d =>
            d.Id == "KVB006" || d.Id == "KVB007" || d.Id == "KVB008");
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "KVBind.UnitTests.GeneratedAssembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new KVBindSourceGenerator());
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
