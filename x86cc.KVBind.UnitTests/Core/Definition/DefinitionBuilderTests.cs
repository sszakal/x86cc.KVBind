using AwesomeAssertions;
using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.InlineSnapshotTesting;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class DefinitionBuilderTests
{
    [Fact]
    public void DefinitionBuilder_WhenFieldGroupAndCollectionAreDeclared_CapturesDefinitions()
    {
        var builder = new KVBindBuilder<BuilderRootNode>();
        builder.Field(x => x.Title, field => field.Required());
        builder.FieldGroup(x => x.General, group => group.Tag("main").Resettable());
        builder.Collection(x => x.Items, collection =>
        {
            collection.Item<BuilderItemNode>(item => item.Field(x => x.Code));
            collection.Item<BuilderSpecialItemNode>("special", item =>
            {
                item.Field(x => x.Code);
                item.Field(x => x.SpecialCode);
            });
        });

        var definition = builder.Build();
        
        InlineSnapshot.Validate(ProjectDefinition(definition), """
            Fields:
              - SubSegmentPath: Title
                IsRequired: true
            Nodes:
              - SubSegmentPath: General
                Fields: []
                Tags:
                  - main
                IsResettable: true
            Collections:
              - SubSegmentPath: Items
                Items:
                  - Token: BuilderItemNode
                    Type: BuilderItemNode
                    Fields:
                      - Code
                  - Token: special
                    Type: BuilderSpecialItemNode
                    Fields:
                      - Code
                      - SpecialCode
                AggregateRules: []
            ValidationRegistrations: []
            Tags: []
            SubSegmentPath:
            """);
    }

    [Fact]
    public void DefinitionBuilder_WhenDefinitionsAreBuilt_ResolversReturnNodeInstancesFromOwner()
    {
        var root = new BuilderRootNode();
        var builder = new KVBindBuilder<BuilderRootNode>();
        builder.FieldGroup(x => x.General);
        builder.Collection(x => x.Items, collection =>
        {
            collection.Item<BuilderItemNode>(item => item.Field(x => x.Code));
        });

        var definition = builder.Build();
        
        InlineSnapshot.Validate(ProjectDefinition(definition), """
            Fields: []
            Nodes:
              - SubSegmentPath: General
                Fields: []
                Tags: []
            Collections:
              - SubSegmentPath: Items
                Items:
                  - Token: BuilderItemNode
                    Type: BuilderItemNode
                    Fields:
                      - Code
                AggregateRules: []
            ValidationRegistrations: []
            Tags: []
            SubSegmentPath:
            """);
    }

    [Fact]
    public void DefinitionBuilder_WhenValidationProfileAndRulesAreDeclared_StoresValidationMetadata()
    {
        var builder = new KVBindBuilder<BuilderRootNode>();
        builder.Field(x => x.Title, field =>
            field.Validation(profile =>
                profile.For<StrictBuilderValidationProfile>(rules => rules.Required().MaxLength(32))));
        builder.Validation(profile =>
            profile.For<StrictBuilderValidationProfile>(group => group.Required(x => x.Title)));
        builder.Collection(x => x.Items, collection =>
        {
            collection.Item<BuilderItemNode>(item =>
            {
                item.Field(x => x.Code);
                item.Field(x => x.Amount);
            });

            collection
                .NotEmpty()
                .MinCount(1)
                .MaxCount(5)
                .AggregateSum("Amount").GreaterThanOrEqual(0);

            collection.Validation(profile =>
                profile.For<StrictBuilderValidationProfile>(rules => rules.MinCount(1)));
        });

        var definition = builder.Build();
        
        InlineSnapshot.Validate(ProjectDefinition(definition), """
            Fields:
              - SubSegmentPath: Title
                ValidationRules: 1
            Nodes: []
            Collections:
              - SubSegmentPath: Items
                Items:
                  - Token: BuilderItemNode
                    Type: BuilderItemNode
                    Fields:
                      - Code
                      - Amount
                NotEmpty: true
                MinCount: 1
                MaxCount: 5
                AggregateRules:
                  - FieldKey: Amount
                    Comparison: GreaterThanOrEqual
                    ErrorCode: aggregate_greater_than_or_equal
                ValidationRules: 2
            ValidationRegistrations:
              - ScopePath: Title
                Rules: 1
            Tags: []
            SubSegmentPath:
            """);
    }

    private static object ProjectDefinition(KVNodeDefinition definition)
    {
        return new
        {
            Fields = definition.Fields.Select(ProjectField).ToArray(),
            Nodes = definition.Nodes.Select(ProjectNode).ToArray(),
            Collections = definition.Collections.Select(ProjectCollection).ToArray(),
            ValidationRegistrations = definition.ValidationRegistrations.Select(registration => new
            {
                registration.ScopePath,
                Rules = registration.Rules.Count
            }).ToArray(),
            Tags = definition.Tags.Order(StringComparer.Ordinal).ToArray(),
            definition.SubSegmentPath
        };
    }

    private static object ProjectNode(KVNodeDefinition definition)
    {
        return new
        {
            definition.SubSegmentPath,
            Fields = definition.Fields.Select(ProjectField).ToArray(),
            Tags = definition.Tags.Order(StringComparer.Ordinal).ToArray(),
            definition.IsResettable
        };
    }

    private static object ProjectField(KVFieldDefinition definition)
    {
        return new
        {
            definition.SubSegmentPath,
            definition.IsRequired,
            ValidationRules = definition.ValidationRules.Count
        };
    }

    private static object ProjectCollection(KVCollectionDefinition definition)
    {
        return new
        {
            definition.SubSegmentPath,
            Items = definition.ItemDefinitionsByToken
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new
                {
                    Token = pair.Key,
                    Type = pair.Value.ModelType.Name,
                    Fields = pair.Value.NodeDefinition.Fields.Select(field => field.SubSegmentPath).ToArray()
                })
                .ToArray(),
            definition.NotEmpty,
            definition.MinCount,
            definition.MaxCount,
            AggregateRules = definition.AggregateRules.Select(rule => new
            {
                rule.FieldKey,
                rule.Comparison,
                rule.ErrorCode
            }).ToArray(),
            ValidationRules = definition.ValidationRules.Count
        };
    }

    private sealed class BuilderRootNode : KVRootNode
    {
        public BuilderGeneralNode General { get; } = new();

        public BuilderCollectionNode Items { get; } = new();

        public string? Title
        {
            get => GetField<string?>("Title");
            set => SetField("Title", value);
        }
    }

    private sealed record StrictBuilderValidationProfile : KVValidationProfile;

    private sealed class BuilderGeneralNode : KVFieldGroupNode;

    private sealed class BuilderCollectionNode : KVCollectionNode<BuilderItemNode>;

    private class BuilderItemNode : KVCollectionItemNode
    {
        public string? Code
        {
            get => GetField<string?>("Code");
            set => SetField("Code", value);
        }

        public decimal Amount
        {
            get => GetField<decimal>("Amount");
            set => SetField("Amount", value);
        }
    }

    private sealed class BuilderSpecialItemNode : BuilderItemNode
    {
        public string? SpecialCode
        {
            get => GetField<string?>("SpecialCode");
            set => SetField("SpecialCode", value);
        }
    }
}
