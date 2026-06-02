namespace x86cc.KVBind.UnitTests.Core;

internal static class TestIds
{
    public static readonly Guid Level1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Level2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Level3 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Level4 = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid Sibling = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public static string Level1Text => Level1.ToString("D");
    public static string Level2Text => Level2.ToString("D");
    public static string Level3Text => Level3.ToString("D");
    public static string Level4Text => Level4.ToString("D");
    public static string SiblingText => Sibling.ToString("D");
}
