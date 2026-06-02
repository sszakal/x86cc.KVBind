using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

internal static class TestDraftCompatibilityExtensions
{
    public static void CommitSetup(this KVRootNode root)
    {
        var model = RootModel(root);
        var commit = model.Overlay.ToCommit(DateTimeOffset.UtcNow);
        model.Snapshot.Apply(commit);
        model.ReplaceOverlay(KVOverlay.Create(model.Snapshot, model.Overlay.User));
    }

    public static void ClearDraft(this KVRootNode root)
    {
        root.Clear();
    }

    public static void CommitOverlay(this KVRootNode root)
    {
        var model = RootModel(root);
        var commit = root.CreateCommit(DateTimeOffset.UtcNow);
        model.Snapshot.Apply(commit);
        model.ReplaceOverlay(KVOverlay.Create(model.Snapshot, model.Overlay.User));
    }

    private static KVModelRoot RootModel(KVNode root)
    {
        return root.Model as KVModelRoot
               ?? throw new InvalidOperationException("Root is not bound to KVModelRoot.");
    }
}
