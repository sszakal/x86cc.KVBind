using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.InlineSnapshotTesting;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;
using x86cc.KVBind.Core;
using x86cc.KVBind.UnitTests.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

static class AssemblyInitializer
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    public static void Initialize()
    {
        InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Default,
            
            MergeTools = [ MergeTool.BeyondCompare ],
            //SnapshotSerializer = new YamlsSnapshotSerializer()
            SnapshotSerializer = new HumanReadableSnapshotSerializer(settings =>
            {
                settings.IncludeFields = false;
                settings.ShowInvisibleCharactersInValues = true;
                settings.IgnoreMember<KVNodeDefinition>(x => x.GetChildNode);
                settings.IgnoreMember<KVCollectionDefinition>(x => x.GetCollection);
                settings.IgnoreMember<KVCompiledValidationRule>(x => x.ProfileMatches);
                settings.IgnoreMember<KVCompiledValidationRule>(x => x.Evaluate);
                settings.Converters.Add(new Func<Type, HumanReadableSerializerOptions,string>((x, _) => x.Name));
                settings.Converters.Add(new Func<List<KVCompiledValidationRule>, HumanReadableSerializerOptions,string>((x, _) => x.Count.ToString()));
                
                settings.AddPropertyAttribute(                                                                                                                                                                                                                                                  
                    property =>                                                                                                                                                                                                                                                                            
                        typeof(KVNode).IsAssignableFrom(property.DeclaringType)                                                                                                                                                                                                                                 
                                      && property.Name is nameof(KVNode.Parent) 
                                          or nameof(KVNode.Model)                                                                                                                                                                                                                     
                                          or nameof(KVNode.Definition),                                                                                                                                                                                                                     
                    new HumanReadableIgnoreAttribute());
                
                settings.AddPropertyAttribute(
                    property =>
                        property.DeclaringType?.IsGenericType == true
                        && property.DeclaringType.GetGenericTypeDefinition() == typeof(KVCollectionNode<>)
                        && property.Name is nameof(KVCollectionNode<>.Parent) 
                            or nameof(KVCollectionNode<>.Model)
                            or nameof(KVCollectionNode<>.Definition),
                    new HumanReadableIgnoreAttribute());
            })
        };
    }
    
    
    public sealed class YamlsSnapshotSerializer : SnapshotSerializer
    {
        /// <inheritdoc/>
        public override string Serialize(object? value)
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            return serializer.Serialize(value);
        }
    }
}
