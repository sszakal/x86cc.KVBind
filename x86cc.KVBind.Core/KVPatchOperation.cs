using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public enum KVPatchOperationType
{
    Set,
    Unset,
    Discard,
    Add,
    Remove,
    Move,
    Init,
    Drop
}

public static class KVPatchOperations
{
    public const string Set = "SET";
    public const string Unset = "UNSET";
    public const string Discard = "DISCARD";
    public const string Add = "ADD";
    public const string Remove = "REMOVE";
    public const string Move = "MOVE";
    public const string Init = "INIT";
    public const string Drop = "DROP";

    private static readonly HashSet<string> BuiltInOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        Set,
        Unset,
        Discard,
        Add,
        Remove,
        Move,
        Init,
        Drop
    };

    public static bool IsBuiltIn(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        return BuiltInOperations.Contains(operation);
    }

    internal static string ToCode(KVPatchOperationType operation)
    {
        return operation switch
        {
            KVPatchOperationType.Set => Set,
            KVPatchOperationType.Unset => Unset,
            KVPatchOperationType.Discard => Discard,
            KVPatchOperationType.Add => Add,
            KVPatchOperationType.Remove => Remove,
            KVPatchOperationType.Move => Move,
            KVPatchOperationType.Init => Init,
            KVPatchOperationType.Drop => Drop,
            _ => throw new InvalidOperationException($"Unknown patch operation '{operation}'.")
        };
    }
}

public sealed class KVPatchOperation
{
    public KVPatchOperation(KVPatchOperationType operation, string path, object? value = null)
        : this(KVPatchOperations.ToCode(operation), path, value, operation)
    {
    }

    public KVPatchOperation(string operation, string path, object? value = null)
        : this(operation, path, value, operationType: null)
    {
    }

    private KVPatchOperation(string operation, string path, object? value, KVPatchOperationType? operationType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateValue(operation, value);

        Operation = operationType;
        OperationCode = operation.ToUpperInvariant();
        Path = path;
        Value = value;
    }

    public KVPatchOperationType? Operation { get; }

    public string OperationCode { get; }

    public string Path { get; }

    public object? Value { get; }

    public static KVPatchOperation Set(string path, object? value) => new(KVPatchOperationType.Set, path, value);

    public static KVPatchOperation Unset(string path) => new(KVPatchOperationType.Unset, path);

    public static KVPatchOperation Discard(string path) => new(KVPatchOperationType.Discard, path);

    public static KVPatchOperation Add(string path, KVAddPatchPayload payload) => new(KVPatchOperationType.Add, path, payload);

    public static KVPatchOperation Remove(string path) => new(KVPatchOperationType.Remove, path);

    public static KVPatchOperation Move(string path, int toIndex) => new(KVPatchOperationType.Move, path, new KVMovePatchPayload(toIndex));

    public static KVPatchOperation Move(string path, KVMovePatchPayload payload) => new(KVPatchOperationType.Move, path, payload);

    public static KVPatchOperation Init(string path, string typeToken) => new(KVPatchOperationType.Init, path, typeToken);

    public static KVPatchOperation Drop(string path) => new(KVPatchOperationType.Drop, path);

    public static KVPatchOperation Custom(string operation, string path, object? value = null) => new(operation, path, value);

    private static void ValidateValue(string operation, object? value)
    {
        switch (operation.ToUpperInvariant())
        {
            case KVPatchOperations.Unset:
            case KVPatchOperations.Discard:
            case KVPatchOperations.Remove:
            case KVPatchOperations.Drop:
                if (value is not null)
                {
                    throw new InvalidOperationException($"Patch operation '{operation}' does not accept values.");
                }
                break;
            case KVPatchOperations.Add:
                if (value is not KVAddPatchPayload payload)
                {
                    throw new InvalidOperationException("ADD patch operations require a KVAddPatchPayload value.");
                }

                if (payload.ItemId == Guid.Empty)
                {
                    throw new InvalidOperationException("ADD patch operations require a non-empty item id.");
                }
                break;
            case KVPatchOperations.Move:
                if (value is not int and not KVMovePatchPayload)
                {
                    throw new InvalidOperationException("MOVE patch operations require an integer target index or KVMovePatchPayload value.");
                }
                break;
            case KVPatchOperations.Init:
                if (value is not string token || string.IsNullOrWhiteSpace(token))
                {
                    throw new InvalidOperationException("INIT patch operations require a non-empty nested-node type token value.");
                }
                break;
        }
    }
}

public sealed record KVAddPatchPayload(Guid ItemId, string? TypeToken = null);

public sealed record KVMovePatchPayload(int ToIndex);
