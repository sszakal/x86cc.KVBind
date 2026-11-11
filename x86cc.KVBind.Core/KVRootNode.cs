using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract class KVRootNode : KVNode
{
    private readonly List<object> pendingEvents = [];

    internal KVReactionExecutionState ReactionExecutionState { get; } = new();

    /// <summary>
    /// Domain events raised (with <c>Raise(...)</c>) on this aggregate or any node beneath it since it was
    /// bound or last dequeued, in the order raised.
    /// </summary>
    public IReadOnlyList<object> PendingEvents => this.pendingEvents;

    public static TSelf Create<TSelf>(KVModelRoot model, KVNodeDefinition definition)
        where TSelf : KVRootNode, new()
    {
        // Authoritative child mark: a marked child must be bound with its parent (which sets inheritance).
        // This funnel catches the parentless overloads; the parent-aware overload checks the converse.
        if (model.Overlay.IsMarkedChild && !model.Overlay.HasInheritance)
            throw new InvalidOperationException("This aggregate is a child and must be bound with its parent snapshot.");

        var node = new TSelf();
        node.Bind(model, definition);
        return node;
    }

    public static TSelf Create<TSelf>(KVOverlay overlay, KVNodeDefinition definition)
        where TSelf : KVRootNode, new()
    {
        return Create<TSelf>(new KVModelRoot(overlay), definition);
    }

    // Creates the aggregate and materializes declared defaults into the (uncommitted) overlay. Use this for a
    // brand-new aggregate; plain Create binds an existing one without touching its values.
    public static TSelf CreateNew<TSelf>(KVModelRoot model, KVNodeDefinition definition)
        where TSelf : KVRootNode, new()
    {
        var node = Create<TSelf>(model, definition);
        node.ApplyDefaults();
        return node;
    }

    public static TSelf CreateNew<TSelf>(KVOverlay overlay, KVNodeDefinition definition)
        where TSelf : KVRootNode, new()
    {
        return CreateNew<TSelf>(new KVModelRoot(overlay), definition);
    }

    // Binds a child aggregate that inherits root members from its parent. Pass the parent's snapshot; the
    // definition's inherited root members then read from it and reject writes. A null parent means "no
    // parent" (a master) — inherited-marked members behave as normal editable fields.
    public static TSelf Create<TSelf>(KVOverlay overlay, KVNodeDefinition definition, KVSnapshot? parentSnapshot)
        where TSelf : KVRootNode, new()
    {
        ApplyInheritance(overlay, definition, parentSnapshot);
        return Create<TSelf>(new KVModelRoot(overlay), definition);
    }

    public static TSelf CreateNew<TSelf>(KVOverlay overlay, KVNodeDefinition definition, KVSnapshot? parentSnapshot)
        where TSelf : KVRootNode, new()
    {
        ApplyInheritance(overlay, definition, parentSnapshot);
        return CreateNew<TSelf>(new KVModelRoot(overlay), definition);
    }

    public KVModelRoot RootModel() => this.Model as KVModelRoot
                                    ?? throw new InvalidOperationException("KVRootNode is not bound to KVModelRoot.");

    /// <summary>
    /// Creates a brand-new child of this aggregate: a fresh, child-marked overlay that inherits this
    /// aggregate's committed (snapshot) values for the definition's inherited members. Apply edits, then
    /// commit and persist the child's snapshot (<see cref="RootModel"/>().Snapshot). The child must be bound
    /// with this parent's snapshot on every subsequent load until it is detached.
    /// </summary>
    public TSelf CreateChild<TSelf>()
        where TSelf : KVRootNode, new()
    {
        var childSnapshot = new KVSnapshot { SchemaVersion = this.Definition.CurrentSchemaVersion };
        var overlay = new KVOverlay(childSnapshot, this.Model.Overlay.User);
        overlay.MarkAsChild(); // staged in the draft → lands in the first commit
        return CreateNew<TSelf>(overlay, this.Definition, this.Model.Overlay.Snapshot.Clone());
    }

    // Fills any unset field/nested/collection default declared in the definition into the overlay. Idempotent
    // (fill-blanks-only); CreateNew calls it for you.
    public void ApplyDefaults() => this.ApplyDefaultsRecursive();

    /// <summary>
    /// Returns the pending domain events and empties the list. Call it when the work that raised them is
    /// persisted — after <see cref="CreateCommit"/>, or after saving a draft that stays open — and publish
    /// or store the result; events live only on this bound instance.
    /// </summary>
    public IReadOnlyList<object> DequeueEvents()
    {
        var events = this.pendingEvents.ToArray();
        this.pendingEvents.Clear();
        return events;
    }

    internal void AddPendingEvent(object domainEvent) => this.pendingEvents.Add(domainEvent);

    // Drops all draft changes, and with them the pending domain events they produced (including any a
    // reaction raises while derived values are recomputed: nothing is left to announce).
    public void Clear()
    {
        this.Model.Overlay.Clear();
        this.Bind(this.Model, this.Definition);
        this.NotifyReplaced();
        this.pendingEvents.Clear();
    }

    // Severs the parent link, copying the inherited values into the draft so they become this aggregate's
    // own editable data (commit to persist). Rebinds so collection/nested membership re-resolves locally.
    public void Detach()
    {
        this.Model.Overlay.Detach();
        this.Bind(this.Model, this.Definition);
    }

    public void Discard(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.Model.Overlay.Discard(KVPath.Normalize(path));
        this.Bind(this.Model, this.Definition);
        this.NotifyReplaced(path);
    }

    /// <summary>
    /// Tells change reactions that the data at <paramref name="path"/> (default: the whole aggregate) was
    /// replaced without per-value notifications, so derived values can be recomputed: every reaction that
    /// watches the path or anything beneath it runs once with <see cref="KVChangeKind.Replaced"/>.
    /// <see cref="Clear"/>, <see cref="Discard"/> and <c>Reset()</c> call this themselves. Call it after
    /// changing the overlay directly — for example after binding a draft whose rebase just finished.
    /// </summary>
    public void NotifyReplaced(string path = "")
    {
        ArgumentNullException.ThrowIfNull(path);
        KVChangeReactionRuntime.EmitReplaced(this, path);
    }

    public KVCommit CreateCommit(DateTimeOffset timestamp)
    {
        var validation = this.Validate();
        if (validation.Errors.Count > 0)
        {
            throw new KVChangeSetValidationException(validation.Errors);
        }

        return this.Model.Overlay.ToCommit(timestamp);
    }

    public KVPatchResult Patch(params KVPatchOperation[] operations)
    {
        KVPatchRuntime.Apply(this, operations);
        var deltas = this.ComputeDeltas(string.Empty, isCollectionItem: false).Flatten();
        return new KVPatchResult(deltas, this.Validate);
    }

    public KVPatchResult Patch(IEnumerable<KVPatchOperation> operations)
    {
        KVPatchRuntime.Apply(this, operations);
        var deltas = this.ComputeDeltas(string.Empty, isCollectionItem: false).Flatten();
        return new KVPatchResult(deltas, this.Validate);
    }

    public KVDraftChanges GetAllChanges()
    {
        var deltas = this.ComputeDeltas(string.Empty, isCollectionItem: false).Flatten();
        return new KVDraftChanges(deltas);
    }

    public KVValidationResult Validate()
    {
        return KVValidationRuntime.Validate(this, this.GetValidationProfile());
    }

    protected override void Bind(KVModel model, KVNodeDefinition definition, KVNodeBase? parent = null)
    {
        if (parent is not null)
            throw new InvalidOperationException("KVRootNode cannot be bound as a child node.");

        if (model is not KVModelRoot)
            throw new InvalidOperationException("KVRootNode must be bound to KVModelRoot.");

        this.Definition = definition;
        base.Bind(model, definition, parent);
    }

    protected virtual KVValidationProfile GetValidationProfile() => KVDefaultValidationProfile.Instance;

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
}
