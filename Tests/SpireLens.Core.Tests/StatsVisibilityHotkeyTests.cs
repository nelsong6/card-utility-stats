using Godot;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class StatsVisibilityHotkeyTests
{
    [Theory]
    [InlineData(Key.Shift, Key.None, KeyLocation.Left, true)]
    [InlineData(Key.None, Key.Shift, KeyLocation.Left, true)]
    [InlineData(Key.Shift, Key.None, KeyLocation.Unspecified, true)]
    [InlineData(Key.Shift, Key.None, KeyLocation.Right, false)]
    [InlineData(Key.A, Key.A, KeyLocation.Left, false)]
    public void IsLeftShiftKey_RequiresLeftShift(
        Key keycode,
        Key physicalKeycode,
        KeyLocation location,
        bool expected)
    {
        Assert.Equal(
            expected,
            LeftShiftTapTracker.IsLeftShiftKey(
                keycode,
                physicalKeycode,
                location));
    }

    [Fact]
    public void Process_LeftShiftTapTogglesOnRelease()
    {
        var tracker = new LeftShiftTapTracker();

        Assert.False(Process(tracker, Key.Shift, pressed: true));
        Assert.True(Process(tracker, Key.Shift, pressed: false));
        Assert.False(Process(tracker, Key.Shift, pressed: false));
    }

    [Theory]
    [InlineData(Key.Tab)]
    [InlineData(Key.Key8)]
    public void Process_ShiftChordDoesNotToggle(Key chordKey)
    {
        var tracker = new LeftShiftTapTracker();

        Assert.False(Process(tracker, Key.Shift, pressed: true));
        Assert.False(Process(tracker, chordKey, pressed: true));
        Assert.False(Process(tracker, chordKey, pressed: false));
        Assert.False(Process(tracker, Key.Shift, pressed: false));
    }

    [Fact]
    public void Process_ModifierHeldBeforeLeftShiftDoesNotToggle()
    {
        var tracker = new LeftShiftTapTracker();

        Assert.False(Process(
            tracker,
            Key.Shift,
            pressed: true,
            otherModifierPressed: true));
        Assert.False(Process(
            tracker,
            Key.Shift,
            pressed: false,
            otherModifierPressed: false));
    }

    [Fact]
    public void Process_ModifierHeldAtLeftShiftReleaseDoesNotToggle()
    {
        var tracker = new LeftShiftTapTracker();

        Assert.False(Process(tracker, Key.Shift, pressed: true));
        Assert.False(Process(
            tracker,
            Key.Shift,
            pressed: false,
            otherModifierPressed: true));
    }

    [Fact]
    public void Process_RightShiftNeverStartsTap()
    {
        var tracker = new LeftShiftTapTracker();

        Assert.False(Process(tracker, Key.Shift, pressed: true, KeyLocation.Right));
        Assert.False(Process(tracker, Key.Shift, pressed: false, KeyLocation.Right));
    }

    [Fact]
    public void Process_IgnoresEchoAndReleaseWithoutPress()
    {
        var tracker = new LeftShiftTapTracker();

        Assert.False(Process(tracker, Key.Shift, pressed: false));
        Assert.False(Process(tracker, Key.Shift, pressed: true, echo: true));
        Assert.False(Process(tracker, Key.Shift, pressed: false));
    }

    [Theory]
    [InlineData(JoyButton.RightStick, true, true)]
    [InlineData(JoyButton.RightStick, false, false)]
    [InlineData(JoyButton.LeftStick, true, false)]
    [InlineData(JoyButton.A, true, false)]
    public void IsRightStickPress_RequiresPressedR3(
        JoyButton buttonIndex,
        bool pressed,
        bool expected)
    {
        Assert.Equal(
            expected,
            StatsVisibilityHotkeyPatch.IsRightStickPress(buttonIndex, pressed));
    }

    private static bool Process(
        LeftShiftTapTracker tracker,
        Key key,
        bool pressed,
        KeyLocation location = KeyLocation.Left,
        bool echo = false,
        bool otherModifierPressed = false)
    {
        return tracker.Process(
            key,
            key,
            location,
            pressed,
            echo,
            otherModifierPressed);
    }
}
