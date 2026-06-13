using AwesomeAssertions;
using Meziantou.Framework.InlineSnapshotTesting;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class FieldGroupBindingTests : KVModelTestBase
{
    public FieldGroupBindingTests()
    {
        RegisterModelDefinition<FieldGroupTestModel>(builder =>
        {
            builder.FieldGroup(x => x.FirstFieldGroup!, fieldGroup =>
            {
                fieldGroup.Field(x => x.Field1);
                fieldGroup.Field(x => x.Field2);
            });
            builder.FieldGroup(x => x.SecondFieldGroup!, fieldGroup =>
            {
                fieldGroup.Field(x => x.Field1);
                fieldGroup.Field(x => x.Field2);
            });
            
            builder.FieldGroup(x => x.NestedFieldGroups!, fieldGroup =>
            {
                fieldGroup.FieldGroup(x => x.FirstFieldGroup!, fieldGroup =>
                {
                    fieldGroup.Field(x => x.Field1);
                    fieldGroup.Field(x => x.Field2);
                });
                fieldGroup.FieldGroup(x => x.SecondFieldGroup!, fieldGroup =>
                {
                    fieldGroup.Field(x => x.Field1);
                    fieldGroup.Field(x => x.Field2);
                });
            });
            builder.Field(x => x.RootName);
        });
    }

    [Fact]
    public void FieldGroupBinding_WhenGroupFieldsAreSet_StoresDraftValuesAtCanonicalPaths()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<FieldGroupTestModel>(model);

        rootNode.FirstFieldGroup.Should().NotBeNull();
        rootNode.SecondFieldGroup.Should().NotBeNull();
        rootNode.FirstFieldGroup.Field1 = "1";
        rootNode.FirstFieldGroup.Field2 = "2";
        rootNode.SecondFieldGroup.Field1 = "3";
        rootNode.SecondFieldGroup.Field2 = "4";
        rootNode.NestedFieldGroups!.FirstFieldGroup!.Field1 = "5";
        rootNode.NestedFieldGroups.FirstFieldGroup.Field2 = "6";
        rootNode.NestedFieldGroups.SecondFieldGroup!.Field1 = "7";
        rootNode.NestedFieldGroups.SecondFieldGroup.Field2 = "8";
        
        model.Overlay.Changes.Should().ContainKey("FirstFieldGroup/Field1").WhoseValue.Should().Be("1");
        model.Overlay.Changes.Should().ContainKey("SecondFieldGroup/Field2").WhoseValue.Should().Be("4");
        model.Overlay.Changes.Should().ContainKey("NestedFieldGroups/FirstFieldGroup/Field1").WhoseValue.Should().Be("5");
        model.Overlay.Changes.Should().ContainKey("NestedFieldGroups/SecondFieldGroup/Field2").WhoseValue.Should().Be("8");
    }   
    
    [Fact]
    public void FieldGroupBinding_WhenValuesExist_ReadsNestedGroupValues()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<FieldGroupTestModel>(model);

        model.Set("FirstFieldGroup/Field1", "1");
        model.Set("FirstFieldGroup/Field2", "2");
        model.Set("SecondFieldGroup/Field1", "3");
        model.Set("SecondFieldGroup/Field2", "4");
        
        model.Set("NestedFieldGroups/FirstFieldGroup/Field1", "5");                                                                                                                                                                                                                     
        model.Set("NestedFieldGroups/FirstFieldGroup/Field2", "6");                                                                                                                                                                                                                     
        model.Set("NestedFieldGroups/SecondFieldGroup/Field1", "7");                                                                                                                                                                                                                    
        model.Set("NestedFieldGroups/SecondFieldGroup/Field2", "8");     
        
        InlineSnapshot.Validate(rootNode, """
            FirstFieldGroup:
              Field1: 1
              Field2: 2
            SecondFieldGroup:
              Field1: 3
              Field2: 4
            NestedFieldGroups:
              FirstFieldGroup:
                Field1: 5
                Field2: 6
              SecondFieldGroup:
                Field1: 7
                Field2: 8
            """);
    }
}

public partial class FieldGroupTestModel : KVRootNode
{
    [KVBind(nameof(FirstFieldGroup))]
    public FieldGroupTestNode? FirstFieldGroup { get; } = new(); 
    
    [KVBind(nameof(SecondFieldGroup))]
    public FieldGroupTestNode? SecondFieldGroup { get; } = new();
    
    [KVBind(nameof(NestedFieldGroups))]
    public AnotherNestedFieldGroupNode? NestedFieldGroups { get; } = new();

    [KVBind(nameof(RootName))]
    public partial string RootName { get; set; }
}

public partial class FieldGroupTestNode : KVFieldGroupNode
{
    [KVBind("Field1")]
    public partial string Field1 { get; set; } 
    
    [KVBind("Field2")]
    public partial string Field2 { get; set; }
}

public partial class AnotherNestedFieldGroupNode : KVFieldGroupNode
{
    [KVBind("FirstFieldGroup")]
    public FieldGroupTestNode? FirstFieldGroup { get; } = new(); 
    
    [KVBind("SecondFieldGroup")]
    public FieldGroupTestNode? SecondFieldGroup { get; } = new();

}
