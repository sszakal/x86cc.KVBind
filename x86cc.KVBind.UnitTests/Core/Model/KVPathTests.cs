using AwesomeAssertions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class KVPathTests
{
    [Theory]
    [InlineData("/a/b/", "a/b")]
    [InlineData("a/b", "a/b")]
    [InlineData("/", "")]
    [InlineData("", "")]
    public void Normalize_TrimsLeadingAndTrailingSlashes(string path, string expected)
    {
        KVPath.Normalize(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("/a/b/", "a/b/")]
    [InlineData("a/b", "a/b")]
    public void NormalizeRelative_TrimsOnlyLeadingSlashes(string path, string expected)
    {
        KVPath.NormalizeRelative(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "b", "b")]
    [InlineData(" ", "b", "b")]
    [InlineData("a", "", "a")]
    [InlineData("a", " ", "a")]
    [InlineData("a", "b", "a/b")]
    [InlineData("a/b", "c", "a/b/c")]
    public void Combine_JoinsWithSlashAndSkipsBlankParts(string basePath, string segment, string expected)
    {
        KVPath.Combine(basePath, segment).Should().Be(expected);
    }

    [Theory]
    [InlineData("a/b", "", true)]
    [InlineData("a", "a", true)]
    [InlineData("a/b", "a", true)]
    [InlineData("a/b/c", "a", true)]
    [InlineData("ab", "a", false)]
    [InlineData("a", "a/b", false)]
    [InlineData("A/b", "a", false)]
    public void IsSameOrDescendant_MatchesWholeSegmentsOrdinally(string path, string ancestorPath, bool expected)
    {
        KVPath.IsSameOrDescendant(path, ancestorPath).Should().Be(expected);
        KVPath.IsSameOrDescendant(path.AsSpan(), ancestorPath).Should().Be(expected, "the span overload must agree with the string overload");
    }

    [Theory]
    [InlineData("a/b", "", "a/b")]
    [InlineData("a", "a", "")]
    [InlineData("a/b", "a", "b")]
    [InlineData("a/b/c", "a", "b/c")]
    [InlineData("a/b/c", "a/b", "c")]
    [InlineData("ab", "a", null)]
    [InlineData("b", "a", null)]
    [InlineData("a", "a/b", null)]
    [InlineData("A/b", "a", null)]
    public void RelativeTo_ReturnsRemainderUnderParentOrNull(string path, string parentPath, string? expected)
    {
        KVPath.RelativeTo(path, parentPath).Should().Be(expected);
    }

    [Theory]
    [InlineData("a/b/c", "a/b")]
    [InlineData("a/b", "a")]
    [InlineData("a", "")]
    [InlineData("", "")]
    public void ParentPath_DropsLastSegment(string path, string expected)
    {
        KVPath.ParentPath(path).Should().Be(expected);
    }
}
