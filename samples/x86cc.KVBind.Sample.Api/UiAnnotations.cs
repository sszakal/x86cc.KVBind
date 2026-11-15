using x86cc.KVBind.Core;

namespace x86cc.KVBind.Sample.Api;

// The application's UI vocabulary, layered over KVBind's neutral annotation bag. KVBind ships none of these
// keys or meanings — they live entirely in the application. The DSL declares a member together with its UI
// hint (`.UiControl("multiselect")`), the schema package carries the annotations to the client, and the UI
// reads them back: the form is generated from the definition yet steerable, without UI concerns leaking
// into the domain model.
public static class UiAnnotations
{
    /// <summary>The input control for a field: text, textarea, number, date, select, radio, multiselect, checkbox.</summary>
    public const string ControlKey = "ui:control";

    /// <summary>How a field group is shown: <c>section</c> renders it inline instead of behind a card.</summary>
    public const string RoleKey = "ui:role";

    /// <summary>A short help text shown under a member.</summary>
    public const string HelpKey = "ui:help";

    /// <summary>Marks a field as the one that names a collection item in lists.</summary>
    public const string TitleKey = "ui:title";

    public static KVFieldOptionsBuilder<TValue> UiControl<TValue>(this KVFieldOptionsBuilder<TValue> field, string control)
        => field.Annotate(ControlKey, control);

    public static KVFieldOptionsBuilder<TValue> ItemTitle<TValue>(this KVFieldOptionsBuilder<TValue> field)
        => field.Annotate(TitleKey, true);

    public static KVFieldGroupOptionsBuilder Section(this KVFieldGroupOptionsBuilder group)
        => group.Annotate(RoleKey, "section");

    public static KVFieldOptionsBuilder<TValue> Help<TValue>(this KVFieldOptionsBuilder<TValue> field, string help)
        => field.Annotate(HelpKey, help);

    public static KVFieldGroupOptionsBuilder Help(this KVFieldGroupOptionsBuilder group, string help)
        => group.Annotate(HelpKey, help);
}
