using System;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode
{
    private KVFieldDefinition GetFieldDefinition(string subSegmentPath)
    {
        return Definition.Fields.Find(f => string.Equals(f.SubSegmentPath, subSegmentPath, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"Field '{subSegmentPath}' is not declared under '{Definition.SubSegmentPath}'.");
    }

    private void EnsureFieldDefined(string subSegmentPath) => _ = GetFieldDefinition(subSegmentPath);

    protected TValue GetField<TValue>(string fieldKey)
    {
        EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        var fieldDefinition = GetFieldDefinition(fieldKey);
        if (fieldDefinition.AllowedValues is not null && Model.TryGetValue(fieldKey, out var storedValue) && storedValue is not null)
        {
            var storedObject = storedValue.Value;
            try
            {
                return (TValue)fieldDefinition.AllowedValues.DenormalizeFromStorage(storedObject, typeof(TValue))!;
            }
            catch (InvalidOperationException) when (storedObject is TValue typed)
            {
                return typed;
            }
        }

        return Model.Get<TValue>(fieldKey);
    }

    protected void SetField<TValue>(string fieldKey, TValue value)
    {
        EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        EnsureFieldDefined(fieldKey);
        SetFieldCore(fieldKey, value);
    }

    internal void SetFieldForPatch(string fieldKey, object? value)
    {
        EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        EnsureFieldDefined(fieldKey);
        SetFieldCore(fieldKey, value);
    }

    internal void RemoveFieldForPatch(string fieldKey)
    {
        EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        EnsureFieldDefined(fieldKey);
        var oldValue = Model.Get<object?>(fieldKey);
        if (!Model.Remove(fieldKey)) return;
        EmitChange(KVPath.Combine(GetCanonicalPath(), fieldKey), oldValue, newValue: null);
    }

    private void SetFieldCore(string fieldKey, object? value)
    {
        var fieldDefinition = GetFieldDefinition(fieldKey);
        var oldValue = Model.Get<object?>(fieldKey);
        var storageValue = value;
        if (fieldDefinition.AllowedValues is not null)
        {
            try
            {
                storageValue = fieldDefinition.AllowedValues.NormalizeForStorage(value);
            }
            catch (InvalidOperationException) when (value is string)
            {
                // Keep unknown tokens in the draft so validation can report allowed_values.
            }
        }

        if (Equals(oldValue, storageValue)) return;

        Model.SetValue(fieldKey, KVValue.FromObject(storageValue));
        EmitChange(KVPath.Combine(GetCanonicalPath(), fieldKey), oldValue, storageValue);
    }
}
