using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class PaintLibraryTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-lib-" + Guid.NewGuid().ToString("N"));
    PaintLibrary Library => new(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    static readonly IReadOnlyList<string?> Sample = ["#FF0000", null, "#00FF00", "#0000FF"];

    [Fact]
    public void A_saved_picture_comes_back_the_same()
    {
        Assert.True(Library.Save("Sunset", Sample, 2));
        Assert.Equal(Sample, Library.Load("Sunset"));
        var entry = Assert.Single(Library.List());
        Assert.Equal(new PaintEntry("Sunset", 4, 2), entry);
    }

    [Fact]
    public void It_survives_a_new_library_object_like_a_restart()
    {
        Assert.True(Library.Save("Keep", Sample, 0));
        Assert.Equal(Sample, new PaintLibrary(_dir).Load("Keep"));
    }

    [Fact]
    public void Saving_under_the_same_name_replaces_it()
    {
        Library.Save("A", Sample, 0);
        Library.Save("A", ["#111111"], 0);
        Assert.Equal(["#111111"], Library.Load("A"));
        Assert.Single(Library.List());
    }

    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("../../evil")]
    [InlineData("C:\\Windows\\evil")]
    [InlineData("a:b*c?d")]
    public void Names_cannot_reach_outside_the_folder(string name)
    {
        Assert.True(Library.Save(name, Sample, 0));
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "evil*"));
        Assert.All(Directory.GetFiles(_dir), f => Assert.Equal(_dir, Path.GetDirectoryName(f)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("....")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData(null)]
    public void Unusable_names_are_refused(string? name) => Assert.False(Library.Save(name!, Sample, 0));

    [Fact]
    public void A_long_name_is_shortened_not_refused()
    {
        Assert.True(Library.Save(new string('x', 200), Sample, 0));
        Assert.Equal(PaintLibrary.MaxNameLength, Library.List()[0].Name.Length);
    }

    [Fact]
    public void An_empty_or_all_blank_picture_is_not_saved()
    {
        Assert.False(Library.Save("E", [], 0));
        Assert.False(Library.Save("E", [null, null], 0));
    }

    [Fact]
    public void A_damaged_or_foreign_file_is_skipped_in_the_list_and_cannot_be_loaded()
    {
        Library.Save("Good", Sample, 0);
        File.WriteAllText(Path.Combine(_dir, "bad.json"), "{ not json");
        File.WriteAllText(Path.Combine(_dir, "other.json"), "{\"format\":\"something-else\",\"paint\":[\"#FFFFFF\"]}");
        Assert.Equal(["Good"], Library.List().Select(e => e.Name));
        Assert.Null(Library.Load("bad"));
        Assert.Null(Library.Load("other"));
    }

    [Fact]
    public void A_huge_file_is_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "huge.json"), new string(' ', PaintFile.MaxBytes + 10));
        Assert.Empty(Library.List());
        Assert.Null(Library.Load("huge"));
    }

    [Fact]
    public void Delete_and_rename_work_and_do_not_overwrite()
    {
        Library.Save("One", Sample, 0);
        Library.Save("Two", ["#222222"], 0);
        Assert.False(Library.Rename("One", "Two"));
        Assert.True(Library.Rename("One", "Uno"));
        Assert.Null(Library.Load("One"));
        Assert.Equal(Sample, Library.Load("Uno"));
        Assert.True(Library.Delete("Uno"));
        Assert.False(Library.Delete("Uno"));
        Assert.Equal(["Two"], Library.List().Select(e => e.Name));
    }

    [Fact]
    public void The_library_is_capped()
    {
        for (var i = 0; i < PaintLibrary.MaxEntries; i++) Assert.True(Library.Save("p" + i, Sample, 0));
        Assert.False(Library.Save("one-too-many", Sample, 0));
        Assert.True(Library.Save("p3", ["#333333"], 0));       // replacing an existing one is still fine
    }

    [Fact]
    public void A_missing_folder_is_an_empty_library_and_a_bad_path_never_throws()
    {
        Assert.Empty(Library.List());
        Assert.Null(Library.Load("nothing"));
        var broken = new PaintLibrary(Path.Combine(_dir, "a\0b"));
        Assert.False(broken.Save("x", Sample, 0));
        Assert.Empty(broken.List());
    }
}

public class CanvasPaintTests
{
    static DevicePlacement Place(string id, double x, double w = 0.2) => new(id, x, 0.4, w, 0.1);

    [Fact]
    public void A_gradient_runs_across_devices_placed_side_by_side()
    {
        var placements = new[] { Place("left", 0.0), Place("right", 0.7) };
        var counts = new Dictionary<string, int> { ["left"] = 4, ["right"] = 4 };
        var paint = CanvasPaint.Gradient(placements, counts, new Dictionary<string, int>(), new Color(1, 0, 0), new Color(0, 0, 1), 0);
        Assert.Equal("#FF0000", paint["left"][0]);
        Assert.Equal("#0000FF", paint["right"][3]);
        Assert.NotEqual(paint["left"][3], paint["right"][0]);         // the gap between them is crossed, not restarted
    }

    [Fact]
    public void Disabled_devices_and_devices_without_leds_are_left_out()
    {
        var placements = new[] { Place("a", 0), Place("off", 0.3) with { Enabled = false }, Place("none", 0.6) };
        var counts = new Dictionary<string, int> { ["a"] = 3, ["off"] = 3, ["none"] = 0 };
        var paint = CanvasPaint.Rainbow(placements, counts, new Dictionary<string, int>(), 0);
        Assert.Equal(["a"], paint.Keys);
        Assert.Equal(3, paint["a"].Count);
    }

    [Fact]
    public void A_single_device_gets_the_full_range()
    {
        var paint = CanvasPaint.Gradient([Place("a", 0.3)], new Dictionary<string, int> { ["a"] = 5 }, new Dictionary<string, int>(), Color.Black, Color.White, 0);
        Assert.Equal("#000000", paint["a"][0]);
        Assert.Equal("#FFFFFF", paint["a"][4]);
    }

    [Fact]
    public void One_led_in_one_spot_does_not_divide_by_zero()
    {
        var paint = CanvasPaint.Gradient([Place("a", 0.3)], new Dictionary<string, int> { ["a"] = 1 }, new Dictionary<string, int>(), new Color(1, 0, 0), new Color(0, 0, 1), 45);
        Assert.Equal("#FF0000", paint["a"][0]);
    }

    [Fact]
    public void A_vertical_angle_follows_the_rows_of_a_keyboard_grid()
    {
        var kb = new DevicePlacement("kb", 0.1, 0.1, 0.5, 0.3);
        var paint = CanvasPaint.Gradient([kb], new Dictionary<string, int> { ["kb"] = 12 }, new Dictionary<string, int> { ["kb"] = 4 }, new Color(1, 0, 0), new Color(0, 0, 1), 90);
        Assert.Equal(paint["kb"][0], paint["kb"][3]);                  // same row, same colour
        Assert.NotEqual(paint["kb"][0], paint["kb"][11]);
    }

    [Fact]
    public void The_rainbow_goes_from_red_to_violet()
    {
        var paint = CanvasPaint.Rainbow([Place("a", 0, 1)], new Dictionary<string, int> { ["a"] = 10 }, new Dictionary<string, int>(), 0);
        Assert.Equal("#FF0000", paint["a"][0]);
        Assert.Equal(PaintTools.RainbowAt(1).ToHex(), paint["a"][9]);
    }
}
