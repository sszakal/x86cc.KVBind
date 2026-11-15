using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class DefaultsTests : KVModelTestBase
{
    public DefaultsTests()
    {
        this.RegisterModelDefinition<DefaultsRoot>(builder =>
        {
            builder.Field(x => x.Status, f => f.Default("draft"));
            builder.Field(x => x.Active, f => f.Default(true));
            builder.Field(x => x.NoDefault); // declared, no default

            builder.FieldGroup(x => x.Settings, settings =>
                settings.Field(x => x.Theme, f => f.Default("dark")));

            builder.Collection(x => x.Lines, lines =>
            {
                lines.Item<LineItem>(item => item.Field(x => x.Label, f => f.Default("untitled")));
                lines.Default(c => c.Create<LineItem>());
            });

            builder.NestedNode(x => x.Owner, owner =>
            {
                owner.DefaultType<PersonOwner>();
                owner.Bind<PersonOwner>("PersonOwner", person => person.Field(x => x.Name, f => f.Default("anonymous")));
                owner.Bind<CompanyOwner>("CompanyOwner", company => company.Field(x => x.CompanyName));
            });
        });
    }

    [Fact]
    public void ApplyDefaults_FillsUnsetScalarFields()
    {
        var root = this.CreateWithDefaults();

        root.Status.Should().Be("draft");
        root.Active.Should().BeTrue();
    }

    [Fact]
    public void ApplyDefaults_LeavesUndeclaredDefaultFieldUnset()
    {
        var root = this.CreateWithDefaults();

        root.NoDefault.Should().BeNull();
    }

    [Fact]
    public void ApplyDefaults_DoesNotOverwriteAnAlreadySetValue()
    {
        var root = this.CreateRoot<DefaultsRoot>();
        root.Status = "approved";

        root.ApplyDefaults();

        root.Status.Should().Be("approved");
    }

    [Fact]
    public void ApplyDefaults_RecursesIntoFieldGroups()
    {
        var root = this.CreateWithDefaults();

        root.Settings.Theme.Should().Be("dark");
    }

    [Fact]
    public void ApplyDefaults_SeedsEmptyCollectionAndDefaultsSeededItemFields()
    {
        var root = this.CreateWithDefaults();

        var line = root.Lines.Should().ContainSingle().Subject;
        line.Label.Should().Be("untitled");
    }

    [Fact]
    public void ApplyDefaults_DoesNotSeedANonEmptyCollection()
    {
        var root = this.CreateRoot<DefaultsRoot>();
        var existing = root.Lines.Create();
        existing.Label = "kept";

        root.ApplyDefaults();

        root.Lines.Should().ContainSingle();
        root.Lines.Single().Label.Should().Be("kept");
    }

    [Fact]
    public void ApplyDefaults_InitializesNestedNodeToDefaultTypeAndAppliesItsFieldDefaults()
    {
        var root = this.CreateWithDefaults();

        var person = root.Owner.Should().BeOfType<PersonOwner>().Subject;
        person.Name.Should().Be("anonymous");
    }

    [Fact]
    public void ApplyDefaults_LeavesAnAlreadyInitializedNestedNodeUntouched()
    {
        var root = this.CreateRoot<DefaultsRoot>();
        root.Patch(KVPatchOperation.Init("/Owner", "CompanyOwner"));

        root.ApplyDefaults();

        root.Owner.Should().BeOfType<CompanyOwner>();
    }

    [Fact]
    public void InitPatch_AppliesTheNewNestedNodesDeclaredDefaults()
    {
        var root = this.CreateRoot<DefaultsRoot>();
        root.Patch(KVPatchOperation.Init("/Owner", "CompanyOwner"));

        root.Patch(KVPatchOperation.Init("/Owner", "PersonOwner"));

        root.Owner.Should().BeOfType<PersonOwner>().Which.Name.Should().Be("anonymous");
    }

    [Fact]
    public void DefaultTypeOfType_UsesTheTokenTheTypeIsBoundWith()
    {
        var definition = BuildOwner(owner => owner
            .DefaultType<PersonOwner>()
            .Bind<PersonOwner>("PERSON", person => person.Field(x => x.Name, f => f.Default("anonymous")))
            .Bind<CompanyOwner>("COMPANY", company => company.Field(x => x.CompanyName)));

        definition.FindNestedNode("Owner")!.DefaultTypeToken.Should().Be("PERSON");

        var root = KVRootNode.CreateNew<DefaultsRoot>(KVOverlay.Create(new KVSnapshot(), "test"), definition);
        root.Owner.Should().BeOfType<PersonOwner>().Which.Name.Should().Be("anonymous");
    }

    [Fact]
    public void DefaultTypeOfType_DeclaredAfterBind_UsesTheBoundToken()
    {
        var definition = BuildOwner(owner => owner
            .Bind<PersonOwner>("PERSON", person => person.Field(x => x.Name))
            .Bind<CompanyOwner>("COMPANY", company => company.Field(x => x.CompanyName))
            .DefaultType<CompanyOwner>());

        definition.FindNestedNode("Owner")!.DefaultTypeToken.Should().Be("COMPANY");
    }

    [Fact]
    public void DefaultTypeOfType_WhenTheTypeIsNotBound_ThrowsWhenTheDefinitionIsBuilt()
    {
        var act = () => BuildOwner(owner => owner
            .DefaultType<CompanyOwner>()
            .Bind<PersonOwner>("PERSON", person => person.Field(x => x.Name)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Nested node 'Owner' declares DefaultType<CompanyOwner>(), but 'CompanyOwner' is not bound*");
    }

    [Fact]
    public void DefaultTypeOfToken_WhenNoSubtypeHasThatToken_ThrowsWhenTheDefinitionIsBuilt()
    {
        var act = () => BuildOwner(owner => owner
            .Bind<PersonOwner>("PERSON", person => person.Field(x => x.Name))
            .DefaultType("PERSN"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Nested node 'Owner' declares DefaultType(\"PERSN\"), but no subtype is bound with that token.");
    }

    [Fact]
    public void DefaultType_TheLastDeclarationWins()
    {
        var definition = BuildOwner(owner => owner
            .Bind<PersonOwner>("PERSON", person => person.Field(x => x.Name))
            .Bind<CompanyOwner>("COMPANY", company => company.Field(x => x.CompanyName))
            .DefaultType("PERSON")
            .DefaultType<CompanyOwner>());

        definition.FindNestedNode("Owner")!.DefaultTypeToken.Should().Be("COMPANY");
    }

    [Fact]
    public void NestedNodeWithoutDefaultType_HasNoDefaultToken()
    {
        var definition = BuildOwner(owner => owner.Bind<PersonOwner>("PERSON", person => person.Field(x => x.Name)));

        definition.FindNestedNode("Owner")!.DefaultTypeToken.Should().BeNull();
    }

    private static KVNodeDefinition BuildOwner(Action<KVNestedNodeOptionsBuilder<OwnerNode>> configure)
    {
        var builder = new KVBindBuilder<DefaultsRoot>();
        builder.NestedNode(x => x.Owner, configure);
        return builder.Build();
    }

    private DefaultsRoot CreateWithDefaults()
    {
        var root = this.CreateRoot<DefaultsRoot>();
        root.ApplyDefaults();
        return root;
    }
}

public partial class DefaultsRoot : KVRootNode
{
    [KVBind("Status")]
    public partial string? Status { get; set; }

    [KVBind("Active")]
    public partial bool Active { get; set; }

    [KVBind("NoDefault")]
    public partial string? NoDefault { get; set; }

    [KVBind("Settings")]
    public DefaultsSettings Settings { get; } = new();

    [KVBind("Lines")]
    public KVCollectionNode<LineItem> Lines { get; } = new();

    [KVBind("Owner")]
    public partial OwnerNode? Owner { get; private set; }
}

public partial class DefaultsSettings : KVFieldGroupNode
{
    [KVBind("Theme")]
    public partial string? Theme { get; set; }
}

public partial class LineItem : KVCollectionItemNode
{
    [KVBind("Label")]
    public partial string? Label { get; set; }
}

public abstract partial class OwnerNode : KVNestedNode;

public partial class PersonOwner : OwnerNode
{
    [KVBind("Name")]
    public partial string? Name { get; set; }
}

public partial class CompanyOwner : OwnerNode
{
    [KVBind("CompanyName")]
    public partial string? CompanyName { get; set; }
}
