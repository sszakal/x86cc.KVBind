using System;
using System.Collections.Generic;
using System.Linq;

namespace x86cc.KVBind.Core;

public class KVFieldOptionsBuilder<TValue>
{
    private readonly List<KVExplicitAllowedValue> explicitAllowedValues = [];
    private readonly List<IKVFieldValidationRuleFactory<TValue>> validationFactories = [];
    private readonly Dictionary<string, object?> annotations = new(StringComparer.Ordinal);

    internal KVAllowedValuesDefinition? AllowedValuesDefinition { get; private set; }

    internal bool IsRequired { get; private set; }

    internal string? DisplayNameValue { get; private set; }

    internal IReadOnlyDictionary<string, object?> Annotations => this.annotations;

    internal bool HasDefault { get; private set; }

    internal object? DefaultValue { get; private set; }

    internal bool IsInherited { get; private set; }

    public KVFieldOptionsBuilder<TValue> Required()
    {
        this.IsRequired = true;
        return this;
    }

    // Root-only: marks this field as inherited — read-only and sourced from the parent when the aggregate
    // is bound with a parent snapshot. Editable on a parentless master.
    public KVFieldOptionsBuilder<TValue> Inherited()
    {
        this.IsInherited = true;
        return this;
    }

    // Create-time default, written into the overlay by ApplyDefaults when the field is unset.
    public KVFieldOptionsBuilder<TValue> Default(TValue value)
    {
        this.HasDefault = true;
        this.DefaultValue = value;
        return this;
    }

    public KVFieldOptionsBuilder<TValue> DisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        this.DisplayNameValue = displayName;
        return this;
    }

    public KVFieldOptionsBuilder<TValue> Annotate(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.annotations[key] = value;
        return this;
    }

    public KVFieldOptionsBuilder<TValue> AllowedValues(params TValue[] values)
    {
        return this.AllowedValues((IEnumerable<TValue>)values);
    }

    public KVFieldOptionsBuilder<TValue> AllowedValues(IEnumerable<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.AllowedValuesDefinition = KVAllowedValuesDefinition.Create(values.ToArray());
        return this;
    }

    public KVFieldOptionsBuilder<TValue> AllowedValues<TToken>(
        IEnumerable<TValue> values,
        Func<TValue, TToken> tokenSelector,
        Func<TValue, string>? labelSelector = null)
        where TToken : notnull
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(tokenSelector);

        this.AllowedValuesDefinition = KVAllowedValuesDefinition.Create(values.ToArray(), tokenSelector, labelSelector);
        return this;
    }

    public KVFieldOptionsBuilder<TValue> AllowedValue(TValue value, string id, string label)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Allowed value id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Allowed value label is required.", nameof(label));
        }

        this.AddExplicitAllowedValue(new KVExplicitAllowedValue(id, label, value, Template: null, Placeholders: null));
        return this;
    }

    public KVFieldOptionsBuilder<TValue> AllowedValueComponent(
        TValue value,
        string id,
        string label,
        Action<KVAllowedValueComponentBuilder> configure)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Allowed value id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Allowed value label is required.", nameof(label));
        }

        ArgumentNullException.ThrowIfNull(configure);

        var component = new KVAllowedValueComponentBuilder();
        configure(component);
        this.AddExplicitAllowedValue(new KVExplicitAllowedValue(id, label, value, component.TemplateValue, component.BuildPlaceholders()));
        return this;
    }

    public KVFieldOptionsBuilder<TValue> AllowedElementValues<TElement, TToken>(
        IEnumerable<TElement> values,
        Func<TElement, TToken> tokenSelector,
        Func<TElement, string>? labelSelector = null)
        where TToken : notnull
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(tokenSelector);

        this.AllowedValuesDefinition = KVAllowedValuesDefinition.CreateForElements(values.ToArray(), tokenSelector, labelSelector);
        return this;
    }

    public KVFieldOptionsBuilder<TValue> AllowedElementValue<TElement>(TElement value, string id, string label)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Allowed value id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Allowed value label is required.", nameof(label));
        }

        this.AddExplicitAllowedElementValue<TElement>(new KVExplicitAllowedValue(id, label, value, Template: null, Placeholders: null));
        return this;
    }

    public KVFieldOptionsBuilder<TValue> AllowedElementValueComponent<TElement>(
        TElement value,
        string id,
        string label,
        Action<KVAllowedValueComponentBuilder> configure)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Allowed value id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Allowed value label is required.", nameof(label));
        }

        ArgumentNullException.ThrowIfNull(configure);

        var component = new KVAllowedValueComponentBuilder();
        configure(component);
        this.AddExplicitAllowedElementValue<TElement>(new KVExplicitAllowedValue(id, label, value, component.TemplateValue, component.BuildPlaceholders()));
        return this;
    }

    public KVFieldOptionsBuilder<TValue> Validation(Action<KVFieldValidationProfileBuilder<TValue>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new KVFieldValidationProfileBuilder<TValue>();
        configure(builder);
        this.AddValidationFactory(builder);
        return this;
    }

    internal IReadOnlyList<KVCompiledValidationRule> BuildValidationRules()
    {
        if (this.validationFactories.Count == 0)
        {
            return [];
        }

        var compiled = new List<KVCompiledValidationRule>();
        foreach (var factory in this.validationFactories)
        {
            compiled.AddRange(factory.Build());
        }

        return compiled;
    }

    internal void AddValidationFactory(IKVFieldValidationRuleFactory<TValue> factory)
    {
        this.validationFactories.Add(factory);
    }

    private void AddExplicitAllowedValue(KVExplicitAllowedValue value)
    {
        if (this.explicitAllowedValues.Any(existing => string.Equals(existing.Id, value.Id, StringComparison.Ordinal)))
        {
            return;
        }

        this.explicitAllowedValues.Add(value);
        this.AllowedValuesDefinition = KVAllowedValuesDefinition.CreateExplicit(this.explicitAllowedValues.ToArray());
    }

    private void AddExplicitAllowedElementValue<TElement>(KVExplicitAllowedValue value)
    {
        if (this.explicitAllowedValues.Any(existing => string.Equals(existing.Id, value.Id, StringComparison.Ordinal)))
        {
            return;
        }

        this.explicitAllowedValues.Add(value);
        this.AllowedValuesDefinition = KVAllowedValuesDefinition.CreateExplicitForElements<TElement>(this.explicitAllowedValues.ToArray());
    }
}
