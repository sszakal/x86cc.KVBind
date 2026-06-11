using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class StaticDefinitionRegistryTests
{
    [Fact]
    public void Get_resolves_static_definition_when_not_explicitly_registered()
    {
        var registry = new KVDefinitionRegistry([]);

        var definition = registry.Get<StaticDefModel>();

        definition.Should().NotBeNull();
        definition.Should().BeSameAs(StaticDefModel.Definition);
    }

    [Fact]
    public void Get_caches_the_resolved_static_definition()
    {
        var registry = new KVDefinitionRegistry([]);

        registry.Get<StaticDefModel>().Should().BeSameAs(registry.Get<StaticDefModel>());
    }

    [Fact]
    public void Get_throws_when_model_is_neither_registered_nor_static()
    {
        var registry = new KVDefinitionRegistry([]);

        var act = () => registry.Get<PlainUnregisteredModel>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*is not registered*");
    }

    [Fact]
    public void Explicit_builder_registration_takes_precedence_over_static_definition()
    {
        var explicitDefinition = BuildNameDefinition();
        var registry = new KVDefinitionRegistry([new InlineBuilder(typeof(StaticDefModel), explicitDefinition)]);

        registry.Get<StaticDefModel>().Should().BeSameAs(explicitDefinition);
        registry.Get<StaticDefModel>().Should().NotBeSameAs(StaticDefModel.Definition);
    }

    [Fact]
    public void Static_definition_binds_and_round_trips_a_field()
    {
        var registry = new KVDefinitionRegistry([]);
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");

        var root = KVRootNode.Create<StaticDefModel>(overlay, registry.Get<StaticDefModel>());
        root.Name = "hello";

        root.Name.Should().Be("hello");
    }

    private static KVNodeDefinition BuildNameDefinition()
    {
        var builder = new KVBindBuilder<StaticDefModel>();
        builder.Field(x => x.Name);
        return builder.Build();
    }

    private sealed class InlineBuilder(Type modelType, KVNodeDefinition definition) : IKVModelDefinitionBuilder
    {
        public Type ModelType => modelType;
        public KVNodeDefinition Build() => definition;
    }
}

public partial class StaticDefModel : KVRootNode, IKVNodeDefinition
{
    [KVBind(nameof(Name))]
    public partial string? Name { get; set; }

    public static KVNodeDefinition Definition { get; } = Build();

    private static KVNodeDefinition Build()
    {
        var builder = new KVBindBuilder<StaticDefModel>();
        builder.Field(x => x.Name);
        return builder.Build();
    }
}

public partial class PlainUnregisteredModel : KVRootNode
{
    [KVBind(nameof(Value))]
    public partial string? Value { get; set; }
}
