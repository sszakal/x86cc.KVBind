using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core.Reactions;

// KVPathPattern compares a changed path with a reaction's pattern segment by segment, in place. Empty
// segments (a leading, trailing or doubled slash) are skipped, as splitting with RemoveEmptyEntries would.
public class PathPatternTests
{
    private static readonly KVPathPattern ItemCost = new(["Parts", "*", "Cost"]);

    [Theory]
    [InlineData("Parts/p1/Cost")]
    [InlineData("/Parts/p1/Cost")]
    [InlineData("Parts/p1/Cost/")]
    [InlineData("Parts//p1/Cost")]
    [InlineData("//Parts/p1//Cost//")]
    public void Matches_IgnoresEmptySegments(string path)
    {
        ItemCost.Matches(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("Parts/p1")]
    [InlineData("Parts/p1/Cost/Extra")]
    [InlineData("Parts/p1/Name")]
    [InlineData("Lines/p1/Cost")]
    [InlineData("")]
    public void Matches_RequiresEverySegmentAndNoMore(string path)
    {
        ItemCost.Matches(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("Parts", true)]
    [InlineData("Parts//p1/", true)]
    [InlineData("Parts/p1/Cost", true)]
    [InlineData("Parts/p1/Cost/Extra", false)]
    [InlineData("Lines", false)]
    public void MatchesAtOrUnder_AcceptsThePathOrAnAncestorOfIt(string path, bool expected)
    {
        ItemCost.MatchesAtOrUnder(path).Should().Be(expected);
    }
}
