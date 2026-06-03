using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

internal static class KVPatchRuntime
{
    internal static void Apply(KVRootNode root, IEnumerable<KVPatchOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(operations);

        var registry = KVPatchOperationRegistry.CreateDefault();
        var context = new KVPatchExecutionContext
        {
            Root = root
        };

        foreach (var operation in operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            var target = KVPatchTargetResolver.Resolve(root, operation);
            registry.Apply(target, operation, context);
        }
    }
}

internal abstract class KVPatchTarget
{
    public required string CanonicalPath { get; init; }
}

internal sealed class KVPathPatchTarget : KVPatchTarget;

internal sealed class KVFieldPatchTarget : KVPatchTarget
{
    public required KVNode Node { get; init; }
    public required KVFieldDefinition Definition { get; init; }
    public required string FieldKey { get; init; }
}

internal sealed class KVCollectionPatchTarget : KVPatchTarget
{
    public required KVNode Owner { get; init; }
    public required IKVCollectionNode Collection { get; init; }
    public required KVCollectionDefinition Definition { get; init; }
}

internal sealed class KVCollectionItemPatchTarget : KVPatchTarget
{
    public required KVNode Owner { get; init; }
    public required IKVCollectionNode Collection { get; init; }
    public required KVCollectionDefinition Definition { get; init; }
    public required KVNode Item { get; init; }
    public required string ItemId { get; init; }
}

internal sealed class KVNestedNodePatchTarget : KVPatchTarget
{
    public required KVNode Owner { get; init; }
    public required KVNestedNodeDefinition Definition { get; init; }
    public required KVModel SlotModel { get; init; }
}

internal sealed class KVPatchExecutionContext
{
    public required KVRootNode Root { get; init; }
}

internal enum KVPatchTargetKind
{
    Path,
    Field,
    Collection,
    CollectionItem,
    NestedNode
}

internal sealed class KVPatchOperationRegistry
{
    private static readonly IReadOnlyDictionary<string, KVPatchOperationDescriptor> BuiltInOperations =
        new Dictionary<string, KVPatchOperationDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [KVPatchOperations.Set] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Set, KVPatchTargetKind.Field, ApplySet),
            [KVPatchOperations.Unset] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Unset, KVPatchTargetKind.Field, ApplyUnset),
            [KVPatchOperations.Add] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Add, KVPatchTargetKind.Collection, ApplyAdd),
            [KVPatchOperations.Remove] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Remove, KVPatchTargetKind.CollectionItem, ApplyRemove),
            [KVPatchOperations.Move] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Move, KVPatchTargetKind.CollectionItem, ApplyMove),
            [KVPatchOperations.Init] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Init, KVPatchTargetKind.NestedNode, ApplyInit),
            [KVPatchOperations.Drop] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Drop, KVPatchTargetKind.NestedNode, ApplyDrop),
            [KVPatchOperations.Discard] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Discard, KVPatchTargetKind.Path, ApplyDiscard)
        };

    public static KVPatchOperationRegistry CreateDefault() => new();

    internal static void EnsureCustomOperation(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (KVPatchOperations.IsBuiltIn(operation))
        {
            throw new InvalidOperationException($"Patch operation '{operation}' is built in and cannot be overridden.");
        }
    }

    internal void Apply(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var descriptor = Resolve(target, operation);
        var targetKind = GetTargetKind(target);
        if (descriptor.TargetKind != targetKind)
        {
            throw Unsupported(target, operation);
        }

        descriptor.Invoke(target, operation, context);
    }

    private static KVPatchOperationDescriptor Resolve(KVPatchTarget target, KVPatchOperation operation)
    {
        if (BuiltInOperations.TryGetValue(operation.OperationCode, out var builtInOperation))
        {
            return builtInOperation;
        }

        if (target is KVCollectionPatchTarget collectionTarget
            && collectionTarget.Definition.PatchOperations.TryGetValue(operation.OperationCode, out var customOperation))
        {
            return customOperation;
        }

        throw Unsupported(target, operation);
    }

    private static KVPatchTargetKind GetTargetKind(KVPatchTarget target)
    {
        return target switch
        {
            KVPathPatchTarget => KVPatchTargetKind.Path,
            KVFieldPatchTarget => KVPatchTargetKind.Field,
            KVCollectionPatchTarget => KVPatchTargetKind.Collection,
            KVCollectionItemPatchTarget => KVPatchTargetKind.CollectionItem,
            KVNestedNodePatchTarget => KVPatchTargetKind.NestedNode,
            _ => throw new InvalidOperationException($"Unknown patch target type '{target.GetType().FullName}'.")
        };
    }

    private static InvalidOperationException Unsupported(KVPatchTarget target, KVPatchOperation operation)
    {
        return new InvalidOperationException($"Patch operation '{operation.OperationCode}' is not valid for path '{operation.Path}' resolved to '{target.CanonicalPath}'.");
    }

    private static void ApplySet(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var fieldTarget = (KVFieldPatchTarget)target;
        var value = operation.Value;
        if (fieldTarget.Definition.AllowedValues is not null && value is not null)
        {
            if (value is string token)
            {
                try
                {
                    value = fieldTarget.Definition.AllowedValues.DenormalizeFromStorage(token);
                }
                catch (InvalidOperationException)
                {
                    // Keep unknown token as-is so validation can report allowed_values.
                }
            }
            else if (!fieldTarget.Definition.AllowedValues.IsAllowed(value))
            {
                throw new InvalidOperationException($"Value for field '{fieldTarget.CanonicalPath}' is not part of configured allowed values.");
            }
        }

        fieldTarget.Node.SetFieldForPatch(fieldTarget.FieldKey, value);
    }

    private static void ApplyUnset(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var fieldTarget = (KVFieldPatchTarget)target;
        fieldTarget.Node.RemoveFieldForPatch(fieldTarget.FieldKey);
    }

    private static void ApplyAdd(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var collectionTarget = (KVCollectionPatchTarget)target;
        var payload = operation.Value as KVAddPatchPayload
                      ?? throw new InvalidOperationException($"ADD patch for '{operation.Path}' requires a KVAddPatchPayload value.");
        collectionTarget.Collection.Create(payload.ItemId, payload.TypeToken);
    }

    private static void ApplyRemove(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var itemTarget = (KVCollectionItemPatchTarget)target;
        itemTarget.Collection.RemoveById(itemTarget.ItemId);
    }

    private static void ApplyMove(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var itemTarget = (KVCollectionItemPatchTarget)target;
        var payload = operation.Value switch
        {
            int toIndex => new KVMovePatchPayload(toIndex),
            KVMovePatchPayload movePayload => movePayload,
            _ => null
        };
        if (payload is null)
        {
            throw new InvalidOperationException($"MOVE patch for '{operation.Path}' requires an integer target index or KVMovePatchPayload value.");
        }

        if (!itemTarget.Collection.MoveById(itemTarget.ItemId, payload.ToIndex))
        {
            throw new InvalidOperationException($"Collection path '{operation.Path}' does not contain child '{itemTarget.ItemId}'.");
        }
    }

    private static void ApplyInit(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var nestedTarget = (KVNestedNodePatchTarget)target;
        if (operation.Value is not string typeToken || string.IsNullOrWhiteSpace(typeToken))
        {
            throw new InvalidOperationException($"INIT patch for '{operation.Path}' requires a nested node type token value.");
        }

        nestedTarget.Definition.GetTypeDefinition(typeToken);
        nestedTarget.Owner.ClearNestedNodeModelForPatch(nestedTarget.SlotModel);
        KVNestedNode.SetItemType(nestedTarget.SlotModel, typeToken);
        nestedTarget.Owner.DetachNestedNodeForPatch(nestedTarget.Definition.SubSegmentPath);
        nestedTarget.Owner.RebindCurrentContextForPatch();
    }

    private static void ApplyDrop(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var nestedTarget = (KVNestedNodePatchTarget)target;
        nestedTarget.Owner.ClearNestedNodeModelForPatch(nestedTarget.SlotModel);
        KVNestedNode.ClearItemType(nestedTarget.SlotModel);
        nestedTarget.Owner.DetachNestedNodeForPatch(nestedTarget.Definition.SubSegmentPath);
        nestedTarget.Owner.RebindCurrentContextForPatch();
    }

    private static void ApplyDiscard(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(target.CanonicalPath))
        {
            context.Root.Clear();
            return;
        }

        context.Root.Discard(target.CanonicalPath);
    }
}

internal sealed class KVPatchOperationDescriptor
{
    private readonly Action<KVPatchTarget, KVPatchOperation, KVPatchExecutionContext> _invoke;

    private KVPatchOperationDescriptor(
        string operation,
        KVPatchTargetKind targetKind,
        Type argumentType,
        Action<KVPatchTarget, KVPatchOperation, KVPatchExecutionContext> invoke)
    {
        Operation = operation;
        TargetKind = targetKind;
        ArgumentType = argumentType;
        _invoke = invoke;
    }

    public string Operation { get; }

    public KVPatchTargetKind TargetKind { get; }

    public Type ArgumentType { get; }

    internal static KVPatchOperationDescriptor BuiltIn(
        string operation,
        KVPatchTargetKind targetKind,
        Action<KVPatchTarget, KVPatchOperation, KVPatchExecutionContext> invoke)
    {
        return new KVPatchOperationDescriptor(operation, targetKind, typeof(KVPatchOperation), invoke);
    }

    internal static KVPatchOperationDescriptor CustomCollection<TParent, TArgument>(string operation, Expression<Func<TParent, Action<TArgument>>> methodSelector)
        where TParent : KVNode
    {
        KVPatchOperationRegistry.EnsureCustomOperation(operation);
        ArgumentNullException.ThrowIfNull(methodSelector);

        var method = GetMethod(methodSelector);
        if (method.ReturnType != typeof(void))
        {
            throw new InvalidOperationException($"Patch operation method '{method.Name}' must return void.");
        }

        var parameters = method.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(TArgument))
        {
            throw new InvalidOperationException($"Patch operation method '{method.Name}' must accept exactly one '{typeof(TArgument).FullName}' argument.");
        }

        var selector = methodSelector.Compile();
        return new KVPatchOperationDescriptor(
            operation.ToUpperInvariant(),
            KVPatchTargetKind.Collection,
            typeof(TArgument),
            (target, patchOperation, context) =>
            {
                var collectionTarget = (KVCollectionPatchTarget)target;
                var argument = CoerceArgument(patchOperation.Value, typeof(TArgument), patchOperation.OperationCode, patchOperation.Path);
                selector((TParent)collectionTarget.Owner)((TArgument)argument!);
            });
    }

    internal static string GetMethodName<TParent, TArgument>(Expression<Func<TParent, Action<TArgument>>> methodSelector)
        where TParent : KVNode
    {
        ArgumentNullException.ThrowIfNull(methodSelector);
        return GetMethod(methodSelector).Name;
    }

    internal void Invoke(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        _invoke(target, operation, context);
    }

    private static MethodInfo GetMethod<TParent, TArgument>(Expression<Func<TParent, Action<TArgument>>> methodSelector)
        where TParent : KVNode
    {
        Expression body = methodSelector.Body;
        if (body is UnaryExpression unaryExpression && unaryExpression.NodeType == ExpressionType.Convert)
        {
            body = unaryExpression.Operand;
        }

        if (body is MethodCallExpression methodCall
            && methodCall.Object is ConstantExpression { Value: MethodInfo methodInfo })
        {
            return methodInfo;
        }

        throw new InvalidOperationException("Patch operation registration must select an instance method group, for example x => x.GroupItems.");
    }

    private static object? CoerceArgument(object? value, Type argumentType, string operation, string path)
    {
        if (value is null)
        {
            if (!argumentType.IsValueType || Nullable.GetUnderlyingType(argumentType) is not null)
            {
                return null;
            }

            throw new InvalidOperationException($"Patch operation '{operation}' for '{path}' requires a '{argumentType.FullName}' payload.");
        }

        if (argumentType.IsInstanceOfType(value))
        {
            return value;
        }

        try
        {
            return value is JsonElement jsonElement
                ? jsonElement.Deserialize(argumentType)
                : JsonSerializer.Deserialize(JsonSerializer.Serialize(value), argumentType);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Patch operation '{operation}' for '{path}' payload cannot be converted to '{argumentType.FullName}'.", exception);
        }
    }
}
