using System;
using System.Collections.Generic;
using System.Linq;

namespace x86cc.KVBind.Core;

public class KVFieldOptionsBuilder<TValue>
{
    private readonly List<KVExplicitAllowedValue> _explicitAllowedValues = [];
    private readonly List<IKVFieldValidationRuleFactory<TValue>> _validationFactories = [];

    internal KVAllowedValuesDefinition? AllowedValuesDefinition { get; private set; }

    internal bool IsRequired { get; private set; }

    internal string? DisplayNameValue { get; private set; }

    public KVFieldOptionsBuilder<TValue> Required()
    {
        IsRequired = true;
        return this;
    }

    public KVFieldOptionsBuilder<TValue> DisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayNameValue = displayName;
        return this;
    }

    public KVFieldOptionsBuilder<TValue> AllowedValues(params TValue[] values)
    {
        return AllowedValues((IEnumerable<TValue>)values);
    }

    public KVFieldOptionsBuilder<TValue> AllowedValues(IEnumerable<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        AllowedValuesDefinition = KVAllowedValuesDefinition.Create(values.ToArray());
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

        AllowedValuesDefinition = KVAllowedValuesDefinition.Create(values.ToArray(), tokenSelector, labelSelector);
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

        AddExplicitAllowedValue(new KVExplicitAllowedValue(id, label, value, Template: null, Placeholders: null));
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
        AddExplicitAllowedValue(new KVExplicitAllowedValue(id, label, value, component.TemplateValue, component.BuildPlaceholders()));
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

        AllowedValuesDefinition = KVAllowedValuesDefinition.CreateForElements(values.ToArray(), tokenSelector, labelSelector);
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

        AddExplicitAllowedElementValue<TElement>(new KVExplicitAllowedValue(id, label, value, Template: null, Placeholders: null));
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
        AddExplicitAllowedElementValue<TElement>(new KVExplicitAllowedValue(id, label, value, component.TemplateValue, component.BuildPlaceholders()));
        return this;
    }

    public KVFieldOptionsBuilder<TValue> Validation(Action<KVFieldValidationProfileBuilder<TValue>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new KVFieldValidationProfileBuilder<TValue>();
        configure(builder);
        AddValidationFactory(builder);
        return this;
    }

    internal IReadOnlyList<KVCompiledValidationRule> BuildValidationRules()
    {
        if (_validationFactories.Count == 0)
        {
            return [];
        }

        var compiled = new List<KVCompiledValidationRule>();
        foreach (var factory in _validationFactories)
        {
            compiled.AddRange(factory.Build());
        }

        return compiled;
    }

    internal void AddValidationFactory(IKVFieldValidationRuleFactory<TValue> factory)
    {
        _validationFactories.Add(factory);
    }

    private void AddExplicitAllowedValue(KVExplicitAllowedValue value)
    {
        if (_explicitAllowedValues.Any(existing => string.Equals(existing.Id, value.Id, StringComparison.Ordinal)))
        {
            return;
        }

        _explicitAllowedValues.Add(value);
        AllowedValuesDefinition = KVAllowedValuesDefinition.CreateExplicit(_explicitAllowedValues.ToArray());
    }

    private void AddExplicitAllowedElementValue<TElement>(KVExplicitAllowedValue value)
    {
        if (_explicitAllowedValues.Any(existing => string.Equals(existing.Id, value.Id, StringComparison.Ordinal)))
        {
            return;
        }

        _explicitAllowedValues.Add(value);
        AllowedValuesDefinition = KVAllowedValuesDefinition.CreateExplicitForElements<TElement>(_explicitAllowedValues.ToArray());
    }
}
