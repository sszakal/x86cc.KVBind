using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public abstract class KVModelTestBase
{
    protected const string TestUser = "test";

    private readonly List<IKVModelDefinitionBuilder> definitionBuilders = [];
    private IKVDefinitionRegistry? registry;

    protected void RegisterModelDefinition<TModel>(Action<KVBindBuilder<TModel>> define)
        where TModel : KVRootNode
    {
        ArgumentNullException.ThrowIfNull(define);

        this.definitionBuilders.Add(new TestModelDefinitionBuilder<TModel>(define));
        this.registry = null;
    }

    protected TKVRootNode CreateRoot<TKVRootNode>(KVModelRoot? model = null)
        where TKVRootNode : KVRootNode, new()
    {
        var modelRegistry = this.CreateRegistry();
        var definition = modelRegistry.Get<TKVRootNode>();
        return KVRootNode.Create<TKVRootNode>(model ?? new KVModelRoot(KVOverlay.Create(new KVSnapshot(), TestUser)), definition);
    }

    protected void CommitSetup(KVModelRoot model)
    {
        var commit = model.Overlay.ToCommit(DateTimeOffset.UtcNow);
        model.Snapshot.Apply(commit);
        model.ReplaceOverlay(KVOverlay.Create(model.Snapshot, model.Overlay.User));
    }

    protected TKVRootNode BindRoot<TKVRootNode>(TKVRootNode root, KVModelRoot model)
        where TKVRootNode : KVRootNode
    {
        var modelRegistry = this.CreateRegistry();
        var definition = modelRegistry.Get<TKVRootNode>();
        root.BindRuntime(model, definition);
        return root;
    }

    protected IKVDefinitionRegistry CreateRegistry()
    {
        return this.registry ??= new KVDefinitionRegistry([.. this.definitionBuilders]);
    }

    private sealed class TestModelDefinitionBuilder<TModel>(Action<KVBindBuilder<TModel>> define)
        : IKVModelDefinitionBuilder
        where TModel : KVRootNode
    {
        public Type ModelType => typeof(TModel);

        public KVNodeDefinition Build()
        {
            var builder = new KVBindBuilder<TModel>();
            define(builder);
            return builder.Build();
        }
    }
}
