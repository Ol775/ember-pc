using Ember.Core;
using Xunit;

namespace Ember.Core.Tests;

public class HotkeyActionsTests
{
    [Fact]
    public void The_fan_mode_cycles_and_wraps()
    {
        Assert.Equal(FanMode.Quiet, HotkeyActions.NextFanMode(FanMode.SuperQuiet));
        Assert.Equal(FanMode.SuperQuiet, HotkeyActions.NextFanMode(FanMode.Extreme));
        Assert.Equal(FanMode.Normal, HotkeyActions.NextFanMode(FanMode.Manual));   // outside the cycle: Normal
    }

    [Fact]
    public void The_scene_cycles_and_a_single_scene_stays()
    {
        Assert.Equal("B", HotkeyActions.NextScene(["A", "B", "C"], "A"));
        Assert.Equal("A", HotkeyActions.NextScene(["A", "B", "C"], "C"));
        Assert.Equal("A", HotkeyActions.NextScene(["A", "B"], "gone"));
        Assert.Null(HotkeyActions.NextScene(["A"], "A"));
        Assert.Null(HotkeyActions.NextScene([], "A"));
    }
}
