using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract class KVRootNode : KVNode
{
    internal KVReactionExecutionState ReactionExecutionState { get; } = new();

    public KVModelRoot RootModel() => Model as KVModelRoot
                                    ?? throw new InvalidOperationException("KVRootNode is not bound to KVModelRoot.");

    protected override void Bind(KVModel model, KVNodeDefinition definition, KVNodeBase? parent = null)
    {
        if (parent is not null)
            throw new InvalidOperationException("KVRootNode cannot be bound as a child node.");

        if (model is not KVModelRoot)
            throw new InvalidOperationException("KVRootNode must be bound to KVModelRoot.");

        Definition = definition;
        base.Bind(model, definition, parent);
    }

    public static TSelf Create<TSelf>(KVModelRoot model, KVNodeDefinition definition) where TSelf : KVRootNode, new()
    {
        // Authoritative child mark: a marked child must be bound with its parent (which sets inheritance).
        // This funnel catches the parentless overloads; the parent-aware overload checks the converse.
        if (model.Overlay.IsMarkedChild && !model.Overlay.HasInheritance)
            throw new InvalidOperationException("This aggregate is a child and must be bound with its parent snapshot.");

        var node = new TSelf();
        node.Bind(model, definition);
        return node;
    }

    public static TSelf Create<TSelf>(KVOverlay overlay, KVNodeDefinition definition) where TSelf : KVRootNode, new()
    {
        return Create<TSelf>(new KVModelRoot(overlay), definition);
    }

    // Creates the aggregate and materializes declared defaults into the (uncommitted) overlay. Use this for a
    // brand-new aggregate; plain Create binds an existing one without touching its values.
    public static TSelf CreateNew<TSelf>(KVModelRoot model, KVNodeDefinition definition) where TSelf : KVRootNode, new()
    {
        var node = Create<TSelf>(model, definition);
        node.ApplyDefaults();
        return node;
    }

    public static TSelf CreateNew<TSelf>(KVOverlay overlay, KVNodeDefinition definition) where TSelf : KVRootNode, new()
    {
        return CreateNew<TSelf>(new KVModelRoot(overlay), definition);
    }

    // Binds a child aggregate that inherits root members from its parent. Pass the parent's snapshot; the
    // definition's inherited root members then read from it and reject writes. A null parent means "no
    // parent" (a master) — inherited-marked members behave as normal editable fields.
    public static TSelf Create<TSelf>(KVOverlay overlay, KVNodeDefinition definition, KVSnapshot? parentSnapshot) where TSelf : KVRootNode, new()
    {
        ApplyInheritance(overlay, definition, parentSnapshot);
        return Create<TSelf>(new KVModelRoot(overlay), definition);
    }

    public static TSelf CreateNew<TSelf>(KVOverlay overlay, KVNodeDefinition definition, KVSnapshot? parentSnapshot) where TSelf : KVRootNode, new()
    {
        ApplyInheritance(overlay, definition, parentSnapshot);
        return CreateNew<TSelf>(new KVModelRoot(overlay), definition);
    }

    private static void ApplyInheritance(KVOverlay overlay, KVNodeDefinition definition, KVSnapshot? parentSnapshot)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(definition);

        // The persisted child mark is authoritative: a child must be bound with its parent, and a parent may
        // only be supplied to a child (so it can't silently shadow a master's own data).
        var isChild = overlay.IsMarkedChild;
        if (isChild && parentSnapshot is null)
            throw new InvalidOperationException("This aggregate is a child and must be bound with its parent snapshot.");
        if (!isChild && parentSnapshot is not null)
            throw new InvalidOperationException("This aggregate is not a child; a parent snapshot cannot be supplied.");

        // Always mark the binding as inherited when a parent is supplied (even with no inherited prefixes), so
        // HasInheritance reliably means "bound as a child" for the funnel check in Create.
        if (parentSnapshot is not null)
            overlay.SetInheritance(parentSnapshot, definition.InheritedPrefixes);
    }

    /// <summary>
    /// Creates a brand-new child of this aggregate: a fresh, child-marked overlay that inherits this
    /// aggregate's committed (snapshot) values for the definition's inherited members. Apply edits, then
    /// commit and persist the child's snapshot (<see cref="RootModel"/>().Snapshot). The child must be bound
    /// with this parent's snapshot on every subsequent load until it is detached.
    /// </summary>
    public TSelf CreateChild<TSelf>() where TSelf : KVRootNode, new()
    {
        var childSnapshot = new KVSnapshot { SchemaVersion = Definition.CurrentSchemaVersion };
        var overlay = new KVOverlay(childSnapshot, Model.Overlay.User);
        overlay.MarkAsChild(); // staged in the draft → lands in the first commit
        return CreateNew<TSelf>(overlay, Definition, Model.Overlay.Snapshot.Clone());
    }

    // Fills any unset field/nested/collection default declared in the definition into the overlay. Idempotent
    // (fill-blanks-only); CreateNew calls it for you.
    public void ApplyDefaults() => ApplyDefaultsRecursive();

    public void Clear()
    {
        Model.Overlay.Clear();
        Bind(Model, Definition);
    }

    // Severs the parent link, copying the inherited values into the draft so they become this aggregate's
    // own editable data (commit to persist). Rebinds so collection/nested membership re-resolves locally.
    public void Detach()
    {
        Model.Overlay.Detach();
        Bind(Model, Definition);
    }

    public void Discard(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Model.Overlay.Discard(KVPath.Normalize(path));
        Bind(Model, Definition);
    }

    public KVCommit CreateCommit(DateTimeOffset timestamp)
    {
        var validation = Validate();
        if (validation.Errors.Count > 0)
        {
            throw new KVChangeSetValidationException(validation.Errors);
        }

        return Model.Overlay.ToCommit(timestamp);
    }

    public KVPatchResult Patch(params KVPatchOperation[] operations)
    {
        KVPatchRuntime.Apply(this, operations);
        var deltas = ComputeDeltas(string.Empty, isCollectionItem: false).Flatten();
        return new KVPatchResult(deltas, Validate);
    }

    public KVPatchResult Patch(IEnumerable<KVPatchOperation> operations)
    {
        KVPatchRuntime.Apply(this, operations);
        var deltas = ComputeDeltas(string.Empty, isCollectionItem: false).Flatten();
        return new KVPatchResult(deltas, Validate);
    }

    public KVDraftChanges GetAllChanges()
    {
        var deltas = ComputeDeltas(string.Empty, isCollectionItem: false).Flatten();
        return new KVDraftChanges(deltas);
    }

    public KVValidationResult Validate()
    {
        return KVValidationRuntime.Validate(this, GetValidationProfile());
    }

    protected virtual KVValidationProfile GetValidationProfile() => KVDefaultValidationProfile.Instance;
}
