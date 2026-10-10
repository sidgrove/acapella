namespace Murmur.Abstractions;

/// <summary>What one key event meant for the push-to-talk chord.</summary>
public enum ChordEvent
{
    /// <summary>Nothing changed.</summary>
    None,

    /// <summary>The chord is now held.</summary>
    Pressed,

    /// <summary>The trigger key came up.</summary>
    Released,
}

/// <summary>
/// Turns raw key events into chord presses and releases, in any key order.
/// </summary>
/// <remarks>
/// <para>
/// A chord such as Win+Ctrl is a <i>set</i> of held keys, not a sequence. Users press the
/// two keys within a few milliseconds of each other in whichever order their fingers land,
/// so the chord is complete the moment the last of its keys goes down — whether that was
/// the trigger or a modifier. Requiring the modifier first meant a Ctrl-then-Win press was
/// missed, and then fired late off Ctrl's key autorepeat, which felt like "sometimes".
/// </para>
/// <para>
/// Releasing either required part ends the chord, so tapping one key again while
/// holding the other reliably produces a fresh press.
/// </para>
/// <para>
/// The match is exact: a modifier outside the chord makes it a different shortcut. Win+Alt+Z
/// bound in AutoHotkey must not dictate on a Win+Ctrl chord, yet it did, because AutoHotkey
/// injects a Ctrl tap to mask the Win key whenever one of its Win or Alt hotkeys fires.
/// AltGr's phantom Left Ctrl is the same hazard.
/// </para>
/// <para>
/// And it stays a different shortcut until every key of the chord has come up. AutoHotkey
/// sends that Ctrl tap as each modifier is released, so with Alt let go first the tap for
/// Win arrives when only Win is held, which on its own looks exactly like Win+Ctrl.
/// </para>
/// <para>
/// Observed events are authoritative. The physical-state delegate seeds keys whose
/// events have not yet been seen, including keys held before the hook was installed.
/// </para>
/// </remarks>
public sealed class ChordDetector
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    private readonly Func<int, bool> _isKeyDown;
    private bool _active;
    private bool _spoiled;
    private readonly Dictionary<int, bool> _observed = [];
    private readonly Dictionary<int, long> _seenAt = [];
    private readonly Func<long> _milliseconds;

    /// <summary>
    /// How long an observed key-down is believed without question. Past it, a key the
    /// physical state says is up had its key-up somewhere the hook could not see: Win+L
    /// ends on the lock screen, and Win then looked held until it was next tapped, so a
    /// bare Ctrl dictated. Long enough that the physical state's lag behind the event in
    /// hand never matters.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2);

    /// <param name="isKeyDown">Whether a virtual key is physically down right now.</param>
    /// <param name="milliseconds">A monotonic clock; the system's when null.</param>
    public ChordDetector(Func<int, bool> isKeyDown, Func<long>? milliseconds = null)
    {
        _isKeyDown = isKeyDown;
        _milliseconds = milliseconds ?? (() => Environment.TickCount64);
    }

    /// <summary>The key whose press and release bracket the recording.</summary>
    public int TriggerKey { get; set; }

    /// <summary>Modifiers that must also be held, as <see cref="HotkeyModifiers"/> flags.</summary>
    public int Modifiers { get; set; }

    /// <summary>Whether the chord is currently held.</summary>
    public bool IsActive => _active;

    /// <summary>Whether <paramref name="key"/> is the trigger or one of the chord's modifiers.</summary>
    public bool Involves(int key) => key == TriggerKey || (FlagOf(key) & Modifiers) != 0;

    /// <summary>Feeds one key event.</summary>
    /// <param name="key">A left/right-specific virtual key.</param>
    /// <param name="isDown">True for key-down, including autorepeat; false for key-up.</param>
    public ChordEvent Feed(int key, bool isDown)
    {
        var repeat = isDown && _observed.ContainsKey(key) && Down(key);
        _observed[key] = isDown;
        _seenAt[key] = _milliseconds();
        var relevant = key == TriggerKey || FlagOf(key) != 0;
        if (!relevant) return ChordEvent.None;
        if (!ChordKeyHeld(key)) _spoiled = false;
        else if (ExtraModifierHeld(key)) _spoiled = true;
        var complete = !_spoiled && Down(TriggerKey) && ModifiersHeld();
        if (_active && !complete)
        {
            _active = false;
            return ChordEvent.Released;
        }
        if (!_active && isDown && !repeat && complete)
        {
            _active = true;
            return ChordEvent.Pressed;
        }
        return ChordEvent.None;
    }

    /// <summary>Forgets observed key state when the hook is reinstalled.</summary>
    public void Reset()
    {
        _active = false;
        _spoiled = false;
        _observed.Clear();
        _seenAt.Clear();
    }

    private bool Down(int key)
    {
        if (!_observed.TryGetValue(key, out var down)) return _isKeyDown(key);
        if (down && _milliseconds() - _seenAt[key] > StaleAfter.TotalMilliseconds && !_isKeyDown(key))
        {
            _observed[key] = false;
            return false;
        }
        return down;
    }

    private bool ModifiersHeld()
    {
        var required = (HotkeyModifiers)Modifiers;
        if (required.HasFlag(HotkeyModifiers.Control) && !Down(VK_LCONTROL) && !Down(VK_RCONTROL)) return false;
        if (required.HasFlag(HotkeyModifiers.Shift) && !Down(VK_LSHIFT) && !Down(VK_RSHIFT)) return false;
        if (required.HasFlag(HotkeyModifiers.Alt) && !Down(VK_LMENU) && !Down(VK_RMENU)) return false;
        if (required.HasFlag(HotkeyModifiers.Windows) && !Down(VK_LWIN) && !Down(VK_RWIN)) return false;
        return true;
    }

    /// <summary>Whether any key of the chord is held; while one is, a spoiled gesture stays spoiled.</summary>
    private bool ChordKeyHeld(int current)
    {
        var required = (HotkeyModifiers)Modifiers;
        return Held(TriggerKey, current)
            || required.HasFlag(HotkeyModifiers.Control) && (Held(VK_LCONTROL, current) || Held(VK_RCONTROL, current))
            || required.HasFlag(HotkeyModifiers.Shift) && (Held(VK_LSHIFT, current) || Held(VK_RSHIFT, current))
            || required.HasFlag(HotkeyModifiers.Alt) && (Held(VK_LMENU, current) || Held(VK_RMENU, current))
            || required.HasFlag(HotkeyModifiers.Windows) && (Held(VK_LWIN, current) || Held(VK_RWIN, current));
    }

    /// <summary>Whether a modifier outside the chord is held, which makes it a different shortcut.</summary>
    private bool ExtraModifierHeld(int current)
    {
        var spare = ~(Modifiers | FlagOf(TriggerKey));
        return (spare & (int)HotkeyModifiers.Control) != 0 && (Held(VK_LCONTROL, current) || Held(VK_RCONTROL, current))
            || (spare & (int)HotkeyModifiers.Shift) != 0 && (Held(VK_LSHIFT, current) || Held(VK_RSHIFT, current))
            || (spare & (int)HotkeyModifiers.Alt) != 0 && (Held(VK_LMENU, current) || Held(VK_RMENU, current))
            || (spare & (int)HotkeyModifiers.Windows) != 0 && (Held(VK_LWIN, current) || Held(VK_RWIN, current));
    }

    /// <summary>
    /// Stricter than <see cref="Down"/>: a key-up missed behind the lock screen or an elevated
    /// window must not leave a phantom modifier blocking the chord, so the physical state has
    /// to agree. The key of the event in hand is the exception, as its physical state lags.
    /// </summary>
    private bool Held(int key, int current)
    {
        if (_observed.TryGetValue(key, out var down) && !down) return false;
        return key == current ? down : _isKeyDown(key);
    }

    private static int FlagOf(int vk) => vk switch
    {
        VK_LCONTROL or VK_RCONTROL or VK_CONTROL => (int)HotkeyModifiers.Control,
        VK_LSHIFT or VK_RSHIFT or VK_SHIFT => (int)HotkeyModifiers.Shift,
        VK_LMENU or VK_RMENU or VK_MENU => (int)HotkeyModifiers.Alt,
        VK_LWIN or VK_RWIN => (int)HotkeyModifiers.Windows,
        _ => 0,
    };
}
