using x86cc.KVBind.Core;

namespace x86cc.KVBind.Sample.Api.Claims;

// Consumer-owned UI vocabulary layered over KVBind's neutral annotation bag. KVBind ships none of these
// keys or meanings — they live entirely in the application. The DSL declares a field together with its UI
// hint (`.UiControl("multiselect")`), and the schema projection reads them back, so the UI is generated
// from the layout yet fully steerable, without leaking UI concerns into the domain definition.
public static class ClaimUiAnnotations
{
    public const string ControlKey = "ui:control";
    public const string RoleKey = "ui:role";

    public static KVFieldOptionsBuilder<TValue> UiControl<TValue>(this KVFieldOptionsBuilder<TValue> field, string control)
        => field.Annotate(ControlKey, control);

    public static KVFieldGroupOptionsBuilder Section(this KVFieldGroupOptionsBuilder group)
        => group.Annotate(RoleKey, "section");

    public static string? ControlOf(this KVFieldDefinition field)
        => field.Annotations.GetValueOrDefault(ClaimUiAnnotations.ControlKey) as string;
}
