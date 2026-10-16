using Ember.Core;
using Xunit;

namespace Ember.Core.Tests;

public class SpecSheetTests
{
    [Theory]
    [InlineData(0UL, "0 B")]
    [InlineData(1536UL, "2 KB")]
    [InlineData(34359738368UL, "32 GB")]
    [InlineData(2048000000000UL, "1.9 TB")]
    public void Bytes_reads_like_the_box(ulong bytes, string expected) => Assert.Equal(expected, SpecSheet.Bytes(bytes));

    [Fact]
    public void Text_lists_groups_and_skips_empty_ones()
    {
        var text = SpecSheet.ToText([
            new SpecGroup("Processor", [new SpecItem("Name", "Test CPU"), new SpecItem("Threads", "16")]),
            new SpecGroup("Empty", [])]);
        Assert.Equal("Processor\n  Name     Test CPU\n  Threads  16".Replace("\n", Environment.NewLine), text);
    }
}
