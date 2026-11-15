using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Sample.Api.Aggregates;

/// <summary>
/// Reads an aggregate's data into a JSON tree by walking its definition, so any model can be sent to a client
/// without per-model DTOs. Fields become properties, field groups objects, collections arrays of items
/// (each with <c>$id</c> and <c>$type</c>), and nested nodes an object with <c>$type</c> (or null).
/// It reads the overlay directly rather than the typed properties, so values a typed getter would reject
/// (an unknown allowed-value token kept for validation) still show.
/// </summary>
public static class AggregateDataProjector
{
    public const string IdKey = "$id";
    public const string TypeKey = "$type";
    private const string ItemsKey = "$items";

    private static readonly JsonSerializerOptions ValueJsonOptions = CreateValueJsonOptions();

    public static JsonObject Project(KVOverlay overlay, KVNodeDefinition definition) =>
        ProjectNode(overlay, definition, prefix: string.Empty);

    /// <summary>The value a path holds, as the client sees it: a scalar, or null when unset.</summary>
    public static JsonNode? ToJson(object? value) =>
        value is null ? null : JsonSerializer.SerializeToNode(value, value.GetType(), ValueJsonOptions);

    public static string Combine(string prefix, string segment) =>
        prefix.Length == 0 ? segment : $"{prefix}/{segment}";

    private static JsonObject ProjectNode(KVOverlay overlay, KVNodeDefinition definition, string prefix)
    {
        var node = new JsonObject();

        foreach (var field in definition.Fields)
        {
            var path = Combine(prefix, field.SubSegmentPath);
            node[field.SubSegmentPath] = overlay.TryGet(path, out var value) && value is not null ? ToJson(value.Value) : null;
        }

        foreach (var group in definition.Nodes)
        {
            node[group.SubSegmentPath] = ProjectNode(overlay, group, Combine(prefix, group.SubSegmentPath));
        }

        foreach (var collection in definition.Collections)
        {
            var collectionPath = Combine(prefix, collection.SubSegmentPath);
            var items = new JsonArray();
            foreach (var itemId in ItemIds(overlay, collectionPath))
            {
                var itemPath = Combine(collectionPath, itemId);
                var token = ReadType(overlay, itemPath)
                            ?? (collection.ItemDefinitionsByToken.Count == 1 ? collection.ItemDefinitionsByToken.Keys.First() : null);
                if (token is null || !collection.ItemDefinitionsByToken.TryGetValue(token, out var itemDefinition))
                {
                    continue;
                }

                var item = ProjectNode(overlay, itemDefinition.NodeDefinition, itemPath);
                item.Insert(0, IdKey, itemId);
                item.Insert(1, TypeKey, token);
                items.Add(item);
            }

            node[collection.SubSegmentPath] = items;
        }

        foreach (var nested in definition.NestedNodes)
        {
            var nestedPath = Combine(prefix, nested.SubSegmentPath);
            var token = ReadType(overlay, nestedPath);
            if (token is null || !nested.TypeDefinitionsByToken.TryGetValue(token, out var typeDefinition))
            {
                node[nested.SubSegmentPath] = null;
                continue;
            }

            var value = ProjectNode(overlay, typeDefinition.NodeDefinition, nestedPath);
            value.Insert(0, TypeKey, token);
            node[nested.SubSegmentPath] = value;
        }

        return node;
    }

    private static string[] ItemIds(KVOverlay overlay, string collectionPath) =>
        overlay.TryGet(Combine(collectionPath, ItemsKey), out var value) && value?.Value is string[] ids ? ids : [];

    private static string? ReadType(KVOverlay overlay, string path) =>
        overlay.TryGet(Combine(path, TypeKey), out var value) && value?.Value is string token && token.Length > 0
            ? token
            : null;

    private static JsonSerializerOptions CreateValueJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
