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

public class DriveSpecTests
{
    static DriveSpec Drive(ulong total, ulong free, long? hours = null) => new("Drive", "SATA hard disk", false, "D:", total, free, "Healthy", true, 45, hours, null);

    [Fact]
    public void Almost_full_means_under_five_percent_free()
    {
        Assert.True(Drive(1000, 49).AlmostFull);
        Assert.False(Drive(1000, 50).AlmostFull);
        Assert.False(Drive(0, 0).AlmostFull);
    }

    [Fact]
    public void Used_fraction_and_age()
    {
        Assert.Equal(0.75, Drive(1000, 250).UsedFraction, 3);
        Assert.Equal("5.3 y", Drive(1, 1, 46771).Age);
        Assert.Equal("", Drive(1, 1).Age);
    }

    [Fact]
    public void Line_lists_what_the_drive_reports()
    {
        var line = new DriveSpec("X", "NVMe SSD", false, "C:", 1_000_000_000_000, 1, "Healthy", true, 42, 1200, 3).ToLine();
        Assert.Equal("Healthy, NVMe SSD, 931 GB, 42 °C, 3% worn, 1,200 h on", line);
    }

    [Fact]
    public void Report_counts()
    {
        var r = new SpecReport([], [Drive(1000, 10), Drive(1000, 900) with { HealthOk = false }]);
        Assert.Equal(1, r.HealthyDrives);
        Assert.Equal(1, r.NearlyFullDrives);
    }
}
