using System;
using System.Collections.Generic;
using System.Globalization;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

internal static class KVValidationRuntime
{
    internal static KVValidationResult Validate(KVRootNode root, KVValidationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(profile);

        var errors = new List<KVValidationError>();
        ValidateNode(root, profile, errors, currentCanonicalPath: string.Empty);
        return new KVValidationResult(errors, [], true);
    }

    private static void ValidateNode(KVNode node, KVValidationProfile profile, List<KVValidationError> errors, string currentCanonicalPath)
    {
        foreach (var field in node.Definition.Fields)
        {
            var path = BuildPath(currentCanonicalPath, field.SubSegmentPath);
            var storagePath = node.ResolveStoragePath(field.SubSegmentPath);
            var value = node.Model.Get<object?>(storagePath);

            if (field.IsRequired && (value is null || (value is string text && string.IsNullOrWhiteSpace(text))))
            {
                errors.Add(new KVValidationError(path, "required", $"'{path}' is required."));
            }

            field.Constraints?.Evaluate(path, value, errors);

            if (field.AllowedValues is not null && value is not null && !field.AllowedValues.IsAllowed(value))
            {
                errors.Add(new KVValidationError(path, "allowed_values", $"Value for field '{path}' is not part of configured allowed values."));
            }

            foreach (var rule in field.ValidationRules)
            {
                if (rule.ProfileMatches(profile))
                {
                    rule.Evaluate(node, path, currentCanonicalPath, errors);
                }
            }
        }

        foreach (var registration in node.Definition.ValidationRegistrations)
        {
            var scopePath = ResolveScopePath(node, registration.ScopePath, currentCanonicalPath);
            foreach (var rule in registration.Rules)
            {
                if (rule.ProfileMatches(profile))
                {
                    rule.Evaluate(node, scopePath, currentCanonicalPath, errors);
                }
            }
        }

        foreach (var collection in node.Definition.Collections)
        {
            ValidateCollection(node, profile, errors, currentCanonicalPath, collection);
        }

        foreach (var nestedNodeDefinition in node.Definition.NestedNodes)
        {
            var nestedPath = BuildPath(currentCanonicalPath, nestedNodeDefinition.SubSegmentPath);
            var nestedModel = node.GetNestedNodeModel(nestedNodeDefinition.SubSegmentPath);
            var activeNode = node.GetActiveNestedNode(nestedNodeDefinition, nestedModel);
            if (activeNode is not null)
            {
                ValidateNode(activeNode, profile, errors, nestedPath);
            }
            else if (nestedNodeDefinition.IsRequired)
            {
                errors.Add(new KVValidationError(nestedPath, "required", $"'{nestedPath}' is required."));
            }
        }

        foreach (var childDefinition in node.Definition.Nodes)
        {
            var child = childDefinition.GetChildNode(node);
            if (child is not null)
            {
                ValidateNode(child, profile, errors, BuildPath(currentCanonicalPath, childDefinition.SubSegmentPath));
            }
        }
    }

    private static void ValidateCollection(KVNode node, KVValidationProfile profile, List<KVValidationError> errors, string currentCanonicalPath, KVCollectionDefinition collection)
    {
        var collectionPath = BuildPath(currentCanonicalPath, collection.SubSegmentPath);
        var collectionNode = collection.GetCollection(node);
        var children = collectionNode.GetActiveItemIds();
        var count = children.Count;

        if (collection.NotEmpty && count == 0)
        {
            errors.Add(new KVValidationError(collectionPath, "not_empty", $"'{collectionPath}' collection cannot be empty."));
        }

        if (collection.MinCount.HasValue && count < collection.MinCount.Value)
        {
            errors.Add(new KVValidationError(collectionPath, "min_count", $"'{collectionPath}' collection must contain at least {collection.MinCount.Value} item(s)."));
        }

        if (collection.MaxCount.HasValue && count > collection.MaxCount.Value)
        {
            errors.Add(new KVValidationError(collectionPath, "max_count", $"'{collectionPath}' collection must contain at most {collection.MaxCount.Value} item(s)."));
        }

        foreach (var aggregateRule in collection.AggregateRules)
        {
            ValidateAggregateRule(errors, collectionPath, collectionNode, children, aggregateRule);
        }

        foreach (var rule in collection.ValidationRules)
        {
            if (rule.ProfileMatches(profile))
            {
                rule.Evaluate(node, collectionPath, currentCanonicalPath, errors);
            }
        }

        foreach (var child in children)
        {
            var itemNode = collectionNode.GetById(child);
            if (itemNode is not null)
            {
                ValidateNode(itemNode, profile, errors, BuildPath(collectionPath, child));
            }
        }
    }

    // Shared by collection-level rules and profile rules. Null values are skipped; a value that is not a number
    // is reported at its item field and suppresses the threshold check, since a partial sum would be misleading.
    internal static void ValidateAggregateRule(List<KVValidationError> errors, string collectionPath, IKVCollectionNode? collectionNode, IReadOnlyList<string> children, KVCollectionAggregateRule aggregateRule)
    {
        decimal sum = 0m;
        var hasNonNumeric = false;
        foreach (var child in children)
        {
            if (collectionNode?.GetById(child) is not { } kvItem) continue;

            var raw = kvItem.Model.Get<object?>(aggregateRule.FieldKey);
            if (raw is null)
            {
                continue;
            }

            if (TryConvertToDecimal(raw, out var value))
            {
                sum += value;
                continue;
            }

            hasNonNumeric = true;
            var fieldPath = BuildPath(BuildPath(collectionPath, child), aggregateRule.FieldKey);
            errors.Add(new KVValidationError(fieldPath, "aggregate_non_numeric", $"'{fieldPath}' value '{raw}' is not a number and cannot be summed for '{collectionPath}'."));
        }

        if (!hasNonNumeric && !Compare(sum, aggregateRule.Threshold, aggregateRule.Comparison))
        {
            errors.Add(new KVValidationError(collectionPath, aggregateRule.ErrorCode, $"'{collectionPath}' aggregate sum for '{aggregateRule.FieldKey}' is invalid."));
        }
    }

    private static bool TryConvertToDecimal(object raw, out decimal value)
    {
        switch (raw)
        {
            case decimal number:
                value = number;
                return true;
            case string text:
                return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
            case bool:
                value = 0m;
                return false;
            case IConvertible convertible:
                try
                {
                    value = convertible.ToDecimal(CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    value = 0m;
                    return false;
                }

            default:
                value = 0m;
                return false;
        }
    }

    private static string ResolveScopePath(KVNode node, string scope, string currentCanonicalPath)
    {
        if (node.Definition.FindField(scope) is not null
            || node.Definition.FindCollection(scope) is not null
            || node.Definition.FindNode(scope) is not null
            || node.Definition.FindNestedNode(scope) is not null)
        {
            return BuildPath(currentCanonicalPath, scope);
        }

        return string.IsNullOrWhiteSpace(currentCanonicalPath)
            ? scope
            : BuildPath(currentCanonicalPath, scope);
    }

    private static bool Compare(decimal value, decimal threshold, KVCollectionAggregateComparison comparison)
    {
        return comparison switch
        {
            KVCollectionAggregateComparison.LessThan => value < threshold,
            KVCollectionAggregateComparison.LessThanOrEqual => value <= threshold,
            KVCollectionAggregateComparison.GreaterThan => value > threshold,
            KVCollectionAggregateComparison.GreaterThanOrEqual => value >= threshold,
            _ => false
        };
    }

    private static string BuildPath(string prefix, string segment)
    {
        return KVPath.Combine(prefix, segment);
    }
}
