using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;

namespace x86cc.KVBind.Sample.Api.DeepForms;

// A bill of materials that can grow without limit: components are collection items, each with its own
// Measurements collection and an empty Detail slot. An ASSEMBLY in that slot holds components again; a
// PURCHASED part ends the branch. Costs roll up level by level through change reactions, so a change to a
// leaf four levels down recomputes every assembly above it and the root total.
//
//   Components/c1/Detail(ASSEMBLY)/Components/c1-1/Detail(ASSEMBLY)/Components/c1-1-1/…/UnitCost

public sealed record DeepFormSubmitted(int Components, decimal TotalCost);

public partial class DeepForm : KVRootNode
{
    [KVBind("Title")]
    public partial string? Title { get; set; }

    [KVBind("Status")]
    public partial string? Status { get; set; }

    [KVBind("TotalCost")]
    public partial decimal TotalCost { get; set; }

    [KVBind("ComponentCount")]
    public partial int ComponentCount { get; set; }

    [KVBind("Components")]
    public KVCollectionNode<DeepComponent> Components { get; } = new();

    /// <summary>Every component in the tree, at any depth.</summary>
    public static int CountAll(KVCollectionNode<DeepComponent> components) =>
        components.Sum(component => 1 + (component.Detail is DeepAssembly assembly ? CountAll(assembly.Components) : 0));

    /// <summary>The number of component levels below the root (0 for an empty tree).</summary>
    public static int DepthOf(KVCollectionNode<DeepComponent> components) =>
        components.Select(component => 1 + (component.Detail is DeepAssembly assembly ? DepthOf(assembly.Components) : 0))
            .DefaultIfEmpty(0)
            .Max();

    public void Submit()
    {
        this.Status = "submitted";
        this.Raise(new DeepFormSubmitted(CountAll(this.Components), this.TotalCost));
    }

    public void Recalculate(KVChangeContext<DeepForm> context)
    {
        this.TotalCost = DeepComponent.Total(this.Components);
        this.ComponentCount = this.Components.Count();
    }
}

public partial class DeepComponent : KVCollectionItemNode
{
    [KVBind("Name")]
    public partial string? Name { get; set; }

    [KVBind("Quantity")]
    public partial int Quantity { get; set; }

    [KVBind("UnitCost")]
    public partial decimal? UnitCost { get; set; }

    [KVBind("Active")]
    public partial bool Active { get; set; }

    [KVBind("DueDate")]
    public partial DateOnly? DueDate { get; set; }

    [KVBind("Category")]
    public partial string? Category { get; set; }

    [KVBind("Tags")]
    public partial string[]? Tags { get; set; }

    [KVBind("Code")]
    public partial string? Code { get; set; }

    [KVBind("Margin")]
    public partial decimal? Margin { get; set; }

    [KVBind("Notes")]
    public partial string? Notes { get; set; }

    [KVBind("Measurements")]
    public KVCollectionNode<DeepMeasurement> Measurements { get; } = new();

    [KVBind("Detail")]
    public partial DeepDetail? Detail { get; private set; }

    /// <summary>What the component costs with everything its detail holds (computed, not stored).</summary>
    public decimal Cost => (this.Quantity * (this.UnitCost ?? 0m)) + (this.Detail?.Subtotal ?? 0m);

    public static decimal Total(KVCollectionNode<DeepComponent> components) => components.Sum(component => component.Cost);
}

public partial class DeepMeasurement : KVCollectionItemNode
{
    [KVBind("Label")]
    public partial string? Label { get; set; }

    [KVBind("Value")]
    public partial decimal? Value { get; set; }

    [KVBind("Unit")]
    public partial string? Unit { get; set; }
}

public abstract partial class DeepDetail : KVNestedNode
{
    /// <summary>The cost the detail adds to its component; maintained by reactions.</summary>
    [KVBind("Subtotal")]
    public partial decimal Subtotal { get; set; }
}

public partial class DeepAssembly : DeepDetail
{
    [KVBind("AssemblyCode")]
    public partial string? AssemblyCode { get; set; }

    [KVBind("Revision")]
    public partial string? Revision { get; set; }

    [KVBind("Weight")]
    public partial decimal? Weight { get; set; }

    [KVBind("Components")]
    public KVCollectionNode<DeepComponent> Components { get; } = new();

    public void Recalculate(KVChangeContext<DeepAssembly> context) => this.Subtotal = DeepComponent.Total(this.Components);
}

public partial class DeepPurchased : DeepDetail
{
    [KVBind("Supplier")]
    public partial string? Supplier { get; set; }

    [KVBind("PartNumber")]
    public partial string? PartNumber { get; set; }

    [KVBind("LeadTimeDays")]
    public partial int? LeadTimeDays { get; set; }

    [KVBind("Price")]
    public partial decimal? Price { get; set; }

    public void Recalculate(KVChangeContext<DeepPurchased> context) => this.Subtotal = this.Price ?? 0m;
}

public sealed class DeepFormDefinitionBuilder : IKVModelDefinitionBuilder
{
    public Type ModelType => typeof(DeepForm);

    public KVNodeDefinition Build()
    {
        // The recursion closes over the nested slot: the ASSEMBLY definition is referenced (SelfReference)
        // by the components it holds, before it is finished.
        var assembly = new KVBindBuilder<DeepAssembly>();
        assembly.Field(x => x.Subtotal, f => f.DisplayName("Subtotal").PatchReadOnly());
        assembly.Field(x => x.AssemblyCode, f => f.DisplayName("Assembly Code").Pattern("ASM-[0-9]{4}"));
        assembly.Field(x => x.Revision, f => f.DisplayName("Revision").MaxLength(10));
        assembly.Field(x => x.Weight, f => f.DisplayName("Weight (kg)").Min(0m));
        assembly.Collection(x => x.Components, components => Components(components, assembly.SelfReference));
        RollUp(assembly, x => x.Components, x => x.Recalculate);
        assembly.Build();

        var builder = new KVBindBuilder<DeepForm>();
        builder.Field(x => x.Title, f => f.DisplayName("Title").Required().MaxLength(120));
        builder.Field(x => x.Status, f => f.DisplayName("Status").Default("draft").PatchReadOnly()
            .AllowedValue("draft", "draft", "Draft").AllowedValue("submitted", "submitted", "Submitted"));
        builder.Field(x => x.TotalCost, f => f.DisplayName("Total Cost").PatchReadOnly());
        builder.Field(x => x.ComponentCount, f => f.DisplayName("Top-level Components").PatchReadOnly());
        builder.Collection(x => x.Components, components => Components(components, assembly.SelfReference));
        RollUp(builder, x => x.Components, x => x.Recalculate);
        builder.Operation("SUBMIT", x => x.Submit);
        return builder.Build();
    }

    private static void Components<TParent>(KVCollectionOptionsBuilder<TParent, DeepComponent> components, KVNodeDefinition assembly)
        where TParent : KVNode
    {
        components.DisplayName("Components");
        components.Item<DeepComponent>("COMPONENT", component =>
        {
            component.Field(x => x.Name, f => f.DisplayName("Name").Required().MaxLength(120).ItemTitle());
            component.Field(x => x.Quantity, f => f.DisplayName("Quantity").Range(0, 10000).Default(1));
            component.Field(x => x.UnitCost, f => f.DisplayName("Unit Cost").Min(0m));
            component.Field(x => x.Active, f => f.DisplayName("Active").Default(true));
            component.Field(x => x.DueDate, f => f.DisplayName("Due Date"));
            component.Field(x => x.Category, f => f.DisplayName("Category")
                .AllowedValue("mechanical", "mechanical", "Mechanical")
                .AllowedValue("electrical", "electrical", "Electrical")
                .AllowedValue("software", "software", "Software")
                .AllowedValue("service", "service", "Service"));
            component.Field(x => x.Tags, f => f.DisplayName("Tags")
                .AllowedElementValue<string>("critical", "critical", "Critical")
                .AllowedElementValue<string>("spare", "spare", "Spare")
                .AllowedElementValue<string>("custom", "custom", "Custom")
                .AllowedElementValue<string>("imported", "imported", "Imported"));
            component.Field(x => x.Code, f => f.DisplayName("Code").Pattern("[A-Z]{3}-[0-9]{3}"));
            component.Field(x => x.Margin, f => f.DisplayName("Margin %").Range(0m, 100m));
            component.Field(x => x.Notes, f => f.DisplayName("Notes").MaxLength(500).UiControl("textarea"));
            component.Collection(x => x.Measurements, measurements =>
            {
                measurements.DisplayName("Measurements");
                measurements.Item<DeepMeasurement>("MEASUREMENT", measurement =>
                {
                    measurement.Field(x => x.Label, f => f.DisplayName("Label").MaxLength(60).ItemTitle());
                    measurement.Field(x => x.Value, f => f.DisplayName("Value"));
                    measurement.Field(x => x.Unit, f => f.DisplayName("Unit")
                        .AllowedValue("mm", "mm", "mm").AllowedValue("kg", "kg", "kg")
                        .AllowedValue("V", "V", "V").AllowedValue("A", "A", "A"));
                });
                measurements.MaxCount(20);
            });
            component.NestedNode(x => x.Detail, detail => detail
                .DisplayName("Detail")
                .Bind<DeepAssembly>("ASSEMBLY", assembly)
                .Bind<DeepPurchased>("PURCHASED", purchased =>
                {
                    purchased.Field(x => x.Subtotal, f => f.DisplayName("Subtotal").PatchReadOnly());
                    purchased.Field(x => x.Supplier, f => f.DisplayName("Supplier").MaxLength(80));
                    purchased.Field(x => x.PartNumber, f => f.DisplayName("Part Number").Pattern("[A-Z]{2}[0-9]{6}"));
                    purchased.Field(x => x.LeadTimeDays, f => f.DisplayName("Lead Time (days)").Range(0, 365));
                    purchased.Field(x => x.Price, f => f.DisplayName("Price").Min(0m));
                    purchased.OnChange(path => path.Field(x => x.Price), x => x.Recalculate);
                }));
        });
        components.MaxCount(100);
    }

    // A node's total follows its components' quantity and unit cost, their membership, and what each
    // component's detail adds (its Subtotal, or the slot being initialized / dropped).
    private static void RollUp<TNode>(
        KVBindBuilder<TNode> node,
        System.Linq.Expressions.Expression<Func<TNode, KVCollectionNode<DeepComponent>>> components,
        System.Linq.Expressions.Expression<Func<TNode, Action<KVChangeContext<TNode>>>> recalculate)
        where TNode : KVNode
    {
        node.OnChange(path => path.Collection(components).Field(x => x.Quantity), recalculate);
        node.OnChange(path => path.Collection(components).Field(x => x.UnitCost), recalculate);
        node.OnChange(path => path.Collection(components).Any(), recalculate);
        node.OnChange(path => path.Collection(components).NestedNode(x => x.Detail).Any(), recalculate);
        node.OnChange(path => path.Collection(components).NestedNode(x => x.Detail).Field(x => x.Subtotal), recalculate);
    }
}
