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
                public ContractGeneral General { get; set; } = new();

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
                public KVCollectionNode<ContractLine> Lines { get; set; } = new();

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
