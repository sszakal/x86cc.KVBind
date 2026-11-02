using System;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode
{
    internal void SetFieldForPatch(string fieldKey, object? value)
    {
        this.EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        this.EnsureFieldDefined(fieldKey);
        this.SetFieldCore(fieldKey, value);
    }

    internal void RemoveFieldForPatch(string fieldKey)
    {
        this.EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        this.EnsureFieldDefined(fieldKey);
        var oldValue = this.Model.Get<object?>(fieldKey);
        if (!this.Model.Remove(fieldKey)) return;
        this.EmitChange(KVPath.Combine(this.GetCanonicalPath(), fieldKey), oldValue, newValue: null);
    }

    protected TValue GetField<TValue>(string fieldKey)
    {
        this.EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        var fieldDefinition = this.GetFieldDefinition(fieldKey);
        if (fieldDefinition.AllowedValues is not null && this.Model.TryGetValue(fieldKey, out var storedValue) && storedValue is not null)
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

        return this.Model.Get<TValue>(fieldKey);
    }

    protected void SetField<TValue>(string fieldKey, TValue value)
    {
        this.EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        var fieldDefinition = this.GetFieldDefinition(fieldKey);

        // Fast path: with no allowed-value remapping the stored type is exactly TValue, so we build the
        // KVValue<TValue> directly instead of going through reflection (KVValue.FromObject) on every write.
        if (fieldDefinition.AllowedValues is null)
        {
            var oldValue = this.Model.Get<object?>(fieldKey);
            if (Equals(oldValue, value)) return;
            this.Model.SetValue(fieldKey, new KVValue<TValue>(value));
            this.EmitChange(KVPath.Combine(this.GetCanonicalPath(), fieldKey), oldValue, value);
            return;
        }

        this.SetFieldCore(fieldKey, value);
    }

    private KVFieldDefinition GetFieldDefinition(string subSegmentPath)
    {
        return this.Definition.FindField(subSegmentPath)
               ?? throw new InvalidOperationException($"Field '{subSegmentPath}' is not declared under '{this.Definition.SubSegmentPath}'.");
    }

    private void EnsureFieldDefined(string subSegmentPath) => _ = this.GetFieldDefinition(subSegmentPath);

    private void SetFieldCore(string fieldKey, object? value)
    {
        var fieldDefinition = this.GetFieldDefinition(fieldKey);
        var oldValue = this.Model.Get<object?>(fieldKey);
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

        this.Model.SetValue(fieldKey, KVValue.FromObject(storageValue));
        this.EmitChange(KVPath.Combine(this.GetCanonicalPath(), fieldKey), oldValue, storageValue);
    }
}
