namespace x86cc.KVBind.Core;

public class KVDefinition
{
    public required string SubSegmentPath { get; init; }

    /// <summary>
    /// Human-friendly label for this field / group / collection / nested node, declared in the DSL via
    /// <c>DisplayName(...)</c> (or a <c>[KVBind(DisplayName = "...")]</c> attribute on the model property).
    /// Consumers fall back to <see cref="SubSegmentPath"/> when this is null.
    /// </summary>
    public string? DisplayName { get; set; }
}