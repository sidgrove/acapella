using System.Runtime.InteropServices;
using Murmur.Abstractions;

namespace Murmur.Platform.Windows;

/// <summary>
/// Types text into whatever application has focus.
/// </summary>
/// <remarks>
/// <para>
/// <b>UI Automation is not an option here.</b> On macOS the Accessibility API can write text
/// directly; Windows has no counterpart. <c>TextPattern</c> is documented as providing no
/// means to insert or modify text, and <c>ValuePattern</c> replaces an entire field rather
/// than inserting at the caret — and multi-line controls do not support it at all. Microsoft's
/// own sample code says plainly: "text input must be simulated."
/// </para>
/// <para>
/// So <c>SendInput</c> is the primary path, not a fallback. Short text is typed as Unicode
/// packets, which leaves the clipboard untouched. Longer text goes via the clipboard, because
/// some terminals and Electron apps drop characters when thousands of synthetic keystrokes
/// arrive back to back.
/// </para>
/// </remarks>
public sealed class SendInputTextInjector : ITextInjector
{
    /// <summary>
    /// Above this many characters, paste instead of typing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Low on purpose. Typing is one message per character and browsers, Teams, Slack and
    /// the like process each one slowly enough that a sentence visibly types itself out.
    /// A paste is one message. Only a word or two is still typed; the app copies every
    /// transcript to the clipboard anyway, so pasting costs nothing that typing kept.
    /// </para>
    /// <para>
    /// Was 40 until 25/09/2026: a 31-character dictation took 405 ms to type, against about
    /// 60 ms for the clipboard settle and one Ctrl+V.
    /// </para>
    /// </remarks>
    private const int PasteThreshold = 12;

    /// <summary>Characters per <c>SendInput</c> call when typing.</summary>
    private const int ChunkSize = 40;

    /// <summary>Pause between chunks, so slower targets keep up.</summary>
    private static readonly TimeSpan ChunkGap = TimeSpan.FromMilliseconds(4);

    /// <summary>
    /// Time for the target to observe the new clipboard before Ctrl+V arrives. Without it, a
    /// fast paste can pick up the <i>previous</i> contents.
    /// </summary>
    private static readonly TimeSpan ClipboardSettle = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// Time for the target to finish processing a paste before Enter is pressed after it.
    /// </summary>
    /// <remarks>
    /// This used to be a 500 ms wait after <i>every</i> paste, left over from when the old
    /// clipboard contents were restored afterwards. The restore went in abfbb8f; the wait
    /// stayed, and cost half a second on every dictation over 40 characters. Only a spoken
    /// send needs the paste to have landed, so only <see cref="SendAsync"/> waits now.
    /// </remarks>
    private static readonly TimeSpan SendSettle = TimeSpan.FromMilliseconds(150);

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const int VK_CONTROL = 0x11;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_V = 0x56;
    private const int VK_RETURN = 0x0D;

    /// <summary>
    /// The modifiers a user might be resting on, each side separately.
    /// </summary>
    /// <remarks>
    /// The side matters. The neutral codes (<c>VK_CONTROL</c> and friends) report "either
    /// side is down", but a synthetic event carrying a neutral code is delivered as the
    /// <i>left</i> key. So lifting a physically held Right Ctrl by sending a neutral up
    /// did nothing — Enter went out as Ctrl+Enter — and the neutral re-press afterwards
    /// pushed a Left Ctrl nobody was holding, which stayed down until the user happened to
    /// tap it. Every keystroke in between was a shortcut.
    /// </remarks>
    private static readonly int[] SidedModifiers =
        [VK_LCONTROL, VK_RCONTROL, VK_LSHIFT, VK_RSHIFT, VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN];

    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParamL, ParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Union;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint GetWindowProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, char[] text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public RECT CaretRect;
    }

    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    /// <inheritdoc />
    /// <remarks>
    /// The foreground window and the control with keyboard focus inside it. Two dictations
    /// that see the same pair went into the same field; a click elsewhere changes it.
    /// </remarks>
    public string? FocusTarget
    {
        get
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;
            var info = new GUITHREADINFO { Size = (uint)Marshal.SizeOf<GUITHREADINFO>() };
            var focus = GetGUIThreadInfo(GetWindowThreadProcessId(window, IntPtr.Zero), ref info) ? info.Focus : IntPtr.Zero;
            return $"{window:X}/{focus:X}";
        }
    }

    /// <inheritdoc />
    public Task<string?> ReadTextBeforeCaretAsync(int maxLength, CancellationToken cancellationToken) =>
        Task.Run(() => UiaCaretReader.Read(maxLength), cancellationToken);

    /// <inheritdoc />
    public Task<string?> ReadTextAroundCaretAsync(int before, int after, CancellationToken cancellationToken) =>
        Task.Run(() => UiaCaretReader.ReadAround(before, after), cancellationToken);

    /// <inheritdoc />
    public FocusedWindow? ForegroundWindow
    {
        get
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;

            var buffer = new char[256];
            var length = GetWindowText(window, buffer, buffer.Length);
            var title = length > 0 ? new string(buffer, 0, length) : string.Empty;

            var app = "unknown";
            try
            {
                if (GetWindowProcessId(window, out var processId) != 0 && processId != 0)
                {
                    using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                    app = process.ProcessName;
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Gone, or not ours to look at; the title alone still helps.
            }
            return new FocusedWindow(app, title);
        }
    }

    /// <summary>Called to place text on the clipboard and paste it.</summary>
    /// <remarks>
    /// Injected rather than called directly because clipboard access is UI-framework specific
    /// and must happen on an STA thread — the app supplies an implementation that marshals to
    /// its own dispatcher.
    /// </remarks>
    public Func<string, CancellationToken, Task<bool>>? ClipboardPaste { get; init; }

    /// <inheritdoc />
    public async ValueTask<bool> InjectAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text)) return true;

        // In Tap mode the recording stops on the chord's key-down, and a short result is
        // ready to type about 60 ms later — while Ctrl and Win are still physically held.
        // The target then reads "N", "e" as Ctrl+N, Ctrl+E and only the letters that arrive
        // after the fingers lift survive: "Neck" landed as "ck" on 2026-09-14, and holding
        // the chord longer lost more. Give the hand time to come off the keys first.
        await WaitForModifiersReleasedAsync(cancellationToken).ConfigureAwait(false);

        // Newlines sent as Unicode packets do not reliably produce a new line — many controls
        // want a real VK_RETURN. Long text goes to the clipboard anyway, which handles them.
        var hasNewlines = text.Contains('\n', StringComparison.Ordinal);

        if (text.Length <= PasteThreshold && !hasNewlines)
        {
            return TypeUnicode(text);
        }

        if (ClipboardPaste is not null)
        {
            return await ClipboardPaste(text, cancellationToken).ConfigureAwait(false);
        }

        return await PasteAsync(text, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts the text on the clipboard and presses Ctrl+V, leaving the transcript available.
    /// Falls back to typing if the clipboard cannot be taken.
    /// </summary>
    private static async Task<bool> PasteAsync(string text, CancellationToken cancellationToken)
    {
        // Keep the completed transcription available for subsequent manual pastes.
        if (!Win32Clipboard.SetText(text)) return TypeWithNewlines(text);

        await Task.Delay(ClipboardSettle, cancellationToken).ConfigureAwait(false);

        // The clipboard keeps the transcript afterwards; nothing is restored, so nothing waits.
        return PressCtrlV();
    }

    /// <summary>Types arbitrary text as Unicode packets.</summary>
    /// <remarks>
    /// <para>
    /// Iterating by UTF-16 code unit is correct and intentional. A non-BMP character — an
    /// emoji, some CJK extensions — is two surrogate halves, and each is sent as its own
    /// event; Unicode-aware controls recombine them. The 16-bit <c>ScanCode</c> field cannot
    /// carry a full 21-bit code point.
    /// </para>
    /// <para>
    /// <b>Failure here is silent.</b> <c>SendInput</c> is subject to UIPI, and Microsoft
    /// documents that when it is blocked "neither GetLastError nor the return value will
    /// indicate the failure was caused by UIPI blocking." Injecting into an elevated window
    /// from a normal process can return success and do nothing.
    /// </para>
    /// </remarks>
    private static bool TypeUnicode(string text)
    {
        var offset = 0;
        while (offset < text.Length)
        {
            var length = Math.Min(ChunkSize, text.Length - offset);

            // Never split a surrogate pair across two SendInput calls. The next chunk then
            // starts after the pair — advancing by the fixed chunk size instead re-sent the
            // low half as a lone surrogate, which lands as U+FFFD.
            if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) length++;

            var inputs = new INPUT[length * 2];
            for (var i = 0; i < length; i++)
            {
                var unit = text[offset + i];
                inputs[i * 2] = UnicodeInput(unit, up: false);
                inputs[(i * 2) + 1] = UnicodeInput(unit, up: true);
            }

            // Lifted around each chunk: a modifier still held after the wait would turn
            // every character into a shortcut.
            if (!PressWithoutHeldModifiers(inputs)) return false;
            offset += length;
            if (offset < text.Length) Thread.Sleep(ChunkGap);
        }

        return true;
    }

    /// <summary>Types text, sending a real Return for each newline.</summary>
    private static bool TypeWithNewlines(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > 0 && !TypeUnicode(lines[i])) return false;

            if (i < lines.Length - 1)
            {
                INPUT[] enter = [KeyInput(VK_RETURN, up: false), KeyInput(VK_RETURN, up: true)];
                if (SendInput(2, enter, InputSize) != 2) return false;
            }
        }

        return true;
    }

    /// <summary>Presses Ctrl+V, temporarily lifting any modifier the user is resting on.</summary>
    /// <remarks>
    /// Microsoft: "This function does not reset the keyboard's current state. Any keys that
    /// are already pressed when the function is called might interfere." A user resting on
    /// Shift would otherwise get Ctrl+Shift+V — "paste as plain text", or something else
    /// entirely depending on the app.
    /// </remarks>
    public static bool PressCtrlV() => PressWithoutHeldModifiers(
        [KeyInput(VK_CONTROL, up: false), KeyInput(VK_V, up: false), KeyInput(VK_V, up: true), KeyInput(VK_CONTROL, up: true)]);

    /// <inheritdoc />
    public async ValueTask<bool> SendAsync(CancellationToken cancellationToken)
    {
        // Let typed or pasted characters reach the target before its submit key.
        await Task.Delay(SendSettle, cancellationToken).ConfigureAwait(false);
        return PressWithoutHeldModifiers([KeyInput(VK_RETURN, up: false), KeyInput(VK_RETURN, up: true)]);
    }

    /// <summary>How long to wait for a held modifier to come up before typing under it anyway.</summary>
    private static readonly TimeSpan ModifierRelease = TimeSpan.FromMilliseconds(800);

    private static async Task WaitForModifiersReleasedAsync(CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)ModifierRelease.TotalMilliseconds;
        var waited = false;
        while (AnyModifierHeld())
        {
            if (Environment.TickCount64 >= deadline)
            {
                // Still held: the characters go out with the modifiers lifted around them
                // instead, so nothing is lost either way.
                PlatformDiagnostics.Warn($"typed with a modifier still held after {ModifierRelease.TotalMilliseconds:0} ms; lifting it around the text");
                return;
            }
            waited = true;
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
        if (waited) PlatformDiagnostics.Warn($"waited {ModifierRelease.TotalMilliseconds - (deadline - Environment.TickCount64):0} ms for the chord to be released before typing");
    }

    private static bool AnyModifierHeld()
    {
        foreach (var key in SidedModifiers)
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Sends <paramref name="keys"/> with every physically held modifier lifted first and
    /// put back afterwards, side-specifically, in one <c>SendInput</c> call.
    /// </summary>
    /// <param name="keys">The events to send in the clear.</param>
    private static bool PressWithoutHeldModifiers(INPUT[] keys)
    {
        var held = new List<int>();
        foreach (var key in SidedModifiers)
        {
            if ((GetAsyncKeyState(key) & 0x8000) != 0) held.Add(key);
        }

        var sequence = new List<INPUT>(held.Count * 2 + keys.Length);
        foreach (var key in held) sequence.Add(KeyInput(key, up: true));
        sequence.AddRange(keys);
        // Re-pressed in the same call, so the gap in which the user could physically let go
        // is a few microseconds; their real key-up afterwards then clears the synthetic one.
        foreach (var key in held) sequence.Add(KeyInput(key, up: false));

        var inputs = sequence.ToArray();
        return SendInput((uint)inputs.Length, inputs, InputSize) == inputs.Length;
    }
    /// <summary>How long to wait after setting the clipboard before pasting.</summary>
    public static TimeSpan ClipboardSettleDelay => ClipboardSettle;

    /// <summary>How long to wait after delivering text before pressing Enter for it.</summary>
    public static TimeSpan SendSettleDelay => SendSettle;

    private static INPUT UnicodeInput(ushort codeUnit, bool up) => new()
    {
        Type = INPUT_KEYBOARD,
        Union = new InputUnion
        {
            Keyboard = new KEYBDINPUT
            {
                VirtualKey = 0,   // must be 0 for KEYEVENTF_UNICODE
                ScanCode = codeUnit,
                Flags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
                Time = 0,
                ExtraInfo = PushToTalkHook.InjectedTag,
            },
        },
    };

    private static INPUT KeyInput(int virtualKey, bool up)
    {
        var flags = up ? KEYEVENTF_KEYUP : 0;
        if (IsExtendedKey(virtualKey)) flags |= KEYEVENTF_EXTENDEDKEY;

        return new INPUT
        {
            Type = INPUT_KEYBOARD,
            Union = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = (ushort)virtualKey,
                    ScanCode = (ushort)MapVirtualKey((uint)virtualKey, MAPVK_VK_TO_VSC),
                    Flags = flags,
                    Time = 0,
                    // Tagged so our own hook ignores it and doesn't re-trigger dictation.
                    ExtraInfo = PushToTalkHook.InjectedTag,
                },
            },
        };
    }

    private static bool IsExtendedKey(int virtualKey) => virtualKey is
        VK_RCONTROL or VK_RMENU or VK_LWIN or VK_RWIN or   // right ctrl/alt, both Windows keys
        0x2D or 0x2E or 0x24 or 0x23 or   // insert, delete, home, end
        0x21 or 0x22 or                   // page up/down
        0x25 or 0x26 or 0x27 or 0x28;     // arrows
}
