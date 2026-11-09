using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using KVBindSourceGenerator = x86cc.KVBind.SourceGenerator.SourceGenerator;

namespace x86cc.KVBind.UnitTests.SourceGenerator;

public class SourceGeneratorCompilationTests
{
    public static TheoryData<string, string> CompilableModels => new()
    {
        {
            "scalar fields",
            """
            public partial class Model : KVRootNode
            {
                [KVBind("Name")] public partial string? Name { get; set; }
                [KVBind("Count")] public partial int Count { get; set; }
                [KVBind("Tags")] public partial List<string>? Tags { get; set; }
            }
            """
        },
        {
            "field with private setter",
            """
            public partial class Model : KVRootNode
            {
                [KVBind("Name")] public partial string? Name { get; private set; }
            }
            """
        },
        {
            "field with init accessor",
            """
            public partial class Model : KVRootNode
            {
                [KVBind("Name")] public partial string? Name { get; init; }
            }
            """
        },
        {
            "get-only field",
            """
            public partial class Model : KVRootNode
            {
                [KVBind("Name")] public partial string? Name { get; }
            }
            """
        },
        {
            "internal model type",
            """
            internal partial class Model : KVRootNode
            {
                [KVBind("Name")] public partial string? Name { get; set; }
            }
            """
        },
        {
            "model nested in another type",
            """
            public static partial class Outer
            {
                public partial class Model : KVRootNode
                {
                    [KVBind("Name")] public partial string? Name { get; set; }
                }
            }
            """
        },
        {
            "internal properties",
            """
            public partial class Model : KVRootNode
            {
                [KVBind("Name")] internal partial string? Name { get; set; }
                [KVBind("Code")] internal partial string? Code { get; private set; }
                [KVBind("Detail")] internal partial Detail? Detail { get; private set; }
            }
            public abstract partial class Detail : KVNestedNode;
            """
        },
        {
            "generic model type",
            """
            public partial class Model<T> : KVRootNode
                where T : class
            {
                [KVBind("Name")] public partial string? Name { get; set; }
            }
            """
        },
        {
            "nested node with init accessor",
            """
            public partial class Model : KVRootNode
            {
                [KVBind("Detail")] public partial Detail? Detail { get; init; }
            }
            public abstract partial class Detail : KVNestedNode;
            """
        },
        {
            "nested node with each setter accessibility",
            """
            public partial class Model : KVRootNode
            {
                [KVBind("A")] public partial Detail? A { get; private set; }
                [KVBind("B")] public partial Detail? B { get; protected set; }
                [KVBind("C")] public partial Detail? C { get; internal set; }
                [KVBind("D")] public partial Detail? D { get; private protected set; }
                [KVBind("E")] public partial Detail? E { get; protected internal set; }
            }
            public abstract partial class Detail : KVNestedNode;
            """
        },
        {
            "collection item and field group",
            """
            public partial class Model : KVRootNode
            {
                [KVBind("Group")] public Group Group { get; } = new();
                [KVBind("Items")] public KVCollectionNode<Item> Items { get; } = new();
            }
            public partial class Group : KVFieldGroupNode
            {
                [KVBind("Code")] public partial string? Code { get; set; }
            }
            public partial class Item : KVCollectionItemNode
            {
                [KVBind("Amount")] public partial decimal Amount { get; set; }
            }
            """
        },
    };

    [Theory]
    [MemberData(nameof(CompilableModels))]
    public void Generator_Output_CompilesWithoutErrors(string scenario, string declarations)
    {
        var source = $$"""
            using System.Collections.Generic;
            using x86cc.KVBind.Core;

            namespace Demo;

            {{declarations}}
            """;

        var (_, outputCompilation) = RunGenerator(CreateCompilation(source));

        outputCompilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .Should().BeEmpty(because: "generated code for '{0}' must compile", scenario);
    }

    [Theory]
    [InlineData("protected", "protected ")]
    [InlineData("internal", "internal ")]
    [InlineData("private protected", "private protected ")]
    [InlineData("protected internal", "protected internal ")]
    public void Generator_NestedNodeSetter_PreservesDeclaredAccessibility(string declared, string emittedPrefix)
    {
        var source = $$"""
            using x86cc.KVBind.Core;
            namespace Demo;
            public partial class Model : KVRootNode
            {
                [KVBind("Detail")]
                public partial Detail? Detail { get; {{declared}} set; }
            }
            public abstract partial class Detail : KVNestedNode;
            """;

        var (driver, _) = RunGenerator(CreateCompilation(source));

        driver.GetRunResult().GeneratedTrees
            .Select(static tree => tree.ToString())
            .Should().ContainSingle(text => text.Contains($"{emittedPrefix}set => SetNestedNode(\"Detail\", value);", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_WhenUnrelatedFileChanges_ReusesCachedModelOutput()
    {
        var modelTree = CSharpSyntaxTree.ParseText("""
            using x86cc.KVBind.Core;
            namespace Demo;
            public partial class Model : KVRootNode
            {
                [KVBind("Name")] public partial string? Name { get; set; }
            }
            """);
        var unrelatedTree = CSharpSyntaxTree.ParseText("namespace Demo; public partial class Unrelated { }");
        var compilation = CreateCompilation(modelTree, unrelatedTree);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new KVBindSourceGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            unrelatedTree,
            CSharpSyntaxTree.ParseText("namespace Demo; public partial class Unrelated { public int X { get; set; } }"));
        driver = driver.RunGenerators(edited);

        var outputs = driver.GetRunResult().Results.Single().TrackedOutputSteps
            .SelectMany(static step => step.Value)
            .SelectMany(static step => step.Outputs)
            .ToArray();

        outputs.Should().NotBeEmpty();
        outputs.Select(static output => output.Reason)
            .Should().OnlyContain(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged);
    }

    private static CSharpCompilation CreateCompilation(string source) => CreateCompilation(CSharpSyntaxTree.ParseText(source));

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] syntaxTrees)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();

        return CSharpCompilation.Create(
            assemblyName: "KVBind.UnitTests.GeneratedAssembly",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static (GeneratorDriver Driver, Compilation Output) RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new KVBindSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return (driver, outputCompilation);
    }
}
