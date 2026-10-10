using Murmur.Abstractions;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>Win+Ctrl must work whichever key lands first, and never fire twice.</summary>
public sealed class ChordDetectorTests
{
    private const int LeftControl = 0xA2;
    private const int Control = 0x11;
    private const int LeftWin = 0x5B;
    private const int LeftAlt = 0xA4;

    /// <summary>Windows delivers low-level hook events before updating asynchronous key state.</summary>
    private sealed class Keyboard
    {
        private readonly HashSet<int> _down = [];

        public ChordDetector Detector { get; }

        public Keyboard(int trigger, HotkeyModifiers modifiers)
        {
            Detector = new ChordDetector(IsDown) { TriggerKey = trigger, Modifiers = (int)modifiers };
        }

        private bool IsDown(int vk) => vk switch
        {
            Control => _down.Contains(0xA2) || _down.Contains(0xA3),
            0x10 => _down.Contains(0xA0) || _down.Contains(0xA1),
            0x12 => _down.Contains(0xA4) || _down.Contains(0xA5),
            _ => _down.Contains(vk),
        };

        public ChordEvent Press(int vk) { var result = Detector.Feed(vk, true); _down.Add(vk); return result; }
        public ChordEvent Repeat(int vk) => Detector.Feed(vk, true);
        public ChordEvent Release(int vk) { var result = Detector.Feed(vk, false); _down.Remove(vk); return result; }
        public void LoseKeyUp(int vk) => _down.Remove(vk);
    }

    [Fact]
    public void Repeated_windows_taps_while_control_is_held_each_complete_the_chord()
    {
        var kb = new Keyboard(LeftControl, HotkeyModifiers.Windows);
        kb.Press(LeftControl);
        for (var i = 0; i < 20; i++)
        {
            kb.Press(LeftWin).ShouldBe(ChordEvent.Pressed);
            kb.Release(LeftWin).ShouldBe(ChordEvent.Released);
            kb.Repeat(LeftControl).ShouldBe(ChordEvent.None);
        }
        kb.Release(LeftControl).ShouldBe(ChordEvent.None);
    }

    [Fact]
    public void Events_remain_authoritative_when_async_key_state_lags()
    {
        var detector = new ChordDetector(_ => false)
        { TriggerKey = LeftControl, Modifiers = (int)HotkeyModifiers.Windows };
        detector.Feed(LeftControl, true).ShouldBe(ChordEvent.None);
        detector.Feed(LeftWin, true).ShouldBe(ChordEvent.Pressed);
        detector.Feed(LeftWin, false).ShouldBe(ChordEvent.Released);
        detector.Feed(LeftControl, false).ShouldBe(ChordEvent.None);
    }
    [Fact]
    public void Modifier_then_trigger_presses_on_the_trigger()
    {
        var kb = new Keyboard(LeftControl, HotkeyModifiers.Windows);

        kb.Press(LeftWin).ShouldBe(ChordEvent.None);
        kb.Press(LeftControl).ShouldBe(ChordEvent.Pressed);
        kb.Release(LeftControl).ShouldBe(ChordEvent.Released);
        kb.Release(LeftWin).ShouldBe(ChordEvent.None);
    }

    [Fact]
    public void Trigger_then_modifier_presses_on_the_modifier()
    {
        var kb = new Keyboard(LeftControl, HotkeyModifiers.Windows);

        kb.Press(LeftControl).ShouldBe(ChordEvent.None, "Win is not held yet");
        kb.Press(LeftWin).ShouldBe(ChordEvent.Pressed, "the chord completed on the modifier");
        kb.Release(LeftWin).ShouldBe(ChordEvent.Released, "releasing either part breaks the chord");
        kb.Release(LeftControl).ShouldBe(ChordEvent.None);
    }

    [Fact]
    public void Autorepeat_never_presses_twice()
    {
        var kb = new Keyboard(LeftControl, HotkeyModifiers.Windows);

        kb.Press(LeftWin);
        kb.Press(LeftControl).ShouldBe(ChordEvent.Pressed);
        kb.Repeat(LeftControl).ShouldBe(ChordEvent.None);
        kb.Repeat(LeftWin).ShouldBe(ChordEvent.None);
        kb.Release(LeftControl).ShouldBe(ChordEvent.Released);
    }

    [Fact]
    public void The_trigger_alone_does_nothing_when_a_modifier_is_required()
    {
        var kb = new Keyboard(LeftControl, HotkeyModifiers.Windows);

        kb.Press(LeftControl).ShouldBe(ChordEvent.None);
        kb.Repeat(LeftControl).ShouldBe(ChordEvent.None);
        kb.Release(LeftControl).ShouldBe(ChordEvent.None, "nothing was pressed, so nothing is released");
    }

    [Fact]
    public void A_modifier_press_that_is_not_part_of_the_chord_is_ignored()
    {
        var kb = new Keyboard(LeftControl, HotkeyModifiers.Windows);

        kb.Press(LeftControl);
        kb.Press(0xA0).ShouldBe(ChordEvent.None, "Shift is not in the chord");
    }

    [Fact]
    public void A_masking_control_tap_under_another_shortcut_does_not_press()
    {
        var kb = new Keyboard(LeftControl, HotkeyModifiers.Windows);

        // Win+Alt+Z bound in AutoHotkey: it injects a Ctrl tap while Win and Alt are held.
        kb.Press(LeftWin);
        kb.Press(LeftAlt);
        kb.Press(0x5A);
        kb.Press(LeftControl).ShouldBe(ChordEvent.None, "Alt is held, so this is not Win+Ctrl");
        kb.Release(LeftControl).ShouldBe(ChordEvent.None);
        kb.Release(0x5A);
        kb.Release(LeftAlt).ShouldBe(ChordEvent.None);
        kb.Release(LeftWin).ShouldBe(ChordEvent.None);
    }

    [Fact]
    public void Releasing_the_extra_modifier_does_not_press_late()
    {
        var kb = new Keyboard(LeftControl, HotkeyModifiers.Windows);

        kb.Press(LeftWin);
        kb.Press(LeftAlt);
        kb.Press(LeftControl).ShouldBe(ChordEvent.None);
        kb.Release(LeftAlt).ShouldBe(ChordEvent.None, "a chord starts on a press, never a release");
        kb.Repeat(LeftControl).ShouldBe(ChordEvent.None);
    }

    [Fact]
    public void An_extra_modifier_pressed_mid_chord_ends_it()
    {
        var kb = new Keyboard(LeftControl, HotkeyModifiers.Windows);

        kb.Press(LeftWin);
        kb.Press(LeftControl).ShouldBe(ChordEvent.Pressed);
        kb.Press(LeftAlt).ShouldBe(ChordEvent.Released);
        kb.Release(LeftControl).ShouldBe(ChordEvent.None);
    }

    [Fact]
    public void A_missed_key_up_of_an_extra_modifier_does_not_block_the_chord()
    {
        var kb = new Keyboard(LeftControl, HotkeyModifiers.Windows);

        // Alt went down, then its key-up happened where the hook could not see it.
        kb.Press(LeftAlt);
        kb.LoseKeyUp(LeftAlt);
        kb.Press(LeftWin);
        kb.Press(LeftControl).ShouldBe(ChordEvent.Pressed);
    }

    [Fact]
    public void The_other_side_of_a_modifier_trigger_is_not_an_extra()
    {
        var kb = new Keyboard(LeftControl, HotkeyModifiers.Windows);

        kb.Press(0xA3);
        kb.Press(LeftWin);
        kb.Press(LeftControl).ShouldBe(ChordEvent.Pressed);
    }

    [Fact]
    public void A_bare_key_needs_no_modifier()
    {
        var kb = new Keyboard(0xA3, HotkeyModifiers.None);

        kb.Press(0xA3).ShouldBe(ChordEvent.Pressed);
        kb.Release(0xA3).ShouldBe(ChordEvent.Released);
    }

    [Fact]
    public void Two_modifiers_complete_in_any_order()
    {
        var kb = new Keyboard(0x20, HotkeyModifiers.Control | HotkeyModifiers.Windows);

        kb.Press(LeftWin).ShouldBe(ChordEvent.None);
        kb.Press(0x20).ShouldBe(ChordEvent.None, "Ctrl is missing");
        kb.Press(LeftControl).ShouldBe(ChordEvent.Pressed);
        kb.Release(0x20).ShouldBe(ChordEvent.Released);
    }
}
