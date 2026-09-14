using System.Runtime.InteropServices;
using Murmur.Abstractions;

namespace Murmur.Platform.Windows;

/// <summary>Restores the native Alt+Space menu for custom title bars.</summary>
/// <remarks>
/// <para>
/// The window draws its own caption, so Windows never sees a title bar to hang the system
/// menu on and Alt+Space does nothing by itself. The first version asked
/// <c>DefWindowProc</c> to behave as if the key had been pressed (<c>SC_KEYMENU</c>), which
/// is what a stock window does internally — but on a window whose caption has been
/// extended into the client area that path did not reliably show anything, so
/// Alt+Space, N (minimise) was dead.
/// </para>
/// <para>
/// This is the way custom-chrome apps (Windows Terminal, Visual Studio) do it: take the
/// window's real system menu, let Windows refresh which items are enabled for the current
/// state, track it as a popup at the caption's top-left, and post the chosen command back
/// to the window. Mnemonics work inside the popup, so Alt+Space then N minimises, X
/// maximises, R restores, C closes.
/// </para>
/// </remarks>
public sealed class WindowMenu : IWindowMenu
{
    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint WM_INITMENU = 0x0116;
    private const int SC_KEYMENU = 0xF100;
    private const int VK_SPACE = 0x20;
    private const uint TPM_LEFTBUTTON = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const int SM_CYCAPTION = 4;
    private const int SM_CXFRAME = 32;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetSystemMenu(nint window, [MarshalAs(UnmanagedType.Bool)] bool revert);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out RECT rect);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    private const uint WM_NULL = 0x0000;
    private const uint WM_KEYFIRST = 0x0100;
    private const uint WM_KEYLAST = 0x0109;
    private const uint PM_REMOVE = 0x0001;
    private static int s_showing;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG message, nint window, uint filterMin, uint filterMax, uint remove);

    /// <summary>Why the last popup did not show, for the log. Null when it did.</summary>
    public string? LastError { get; private set; }

    private const int VK_MENU = 0x12;

    /// <summary>How long to wait for Alt and Space to come up before opening the menu.</summary>
    private static readonly TimeSpan ReleaseWait = TimeSpan.FromMilliseconds(600);

    /// <inheritdoc />
    public int Show(nint handle)
    {
        if (handle == 0) return 0;

        // The key-down that opens the menu autorepeats while the chord is held, and each
        // repeat arrives as another request. Only the first one may run.
        if (Interlocked.CompareExchange(ref s_showing, 1, 0) != 0) return 0;
        try { return ShowOnce(handle); }
        finally { s_showing = 0; }
    }

    private int ShowOnce(nint handle)
    {
        LastError = null;
        var menu = GetSystemMenu(handle, revert: false);
        if (menu == 0)
        {
            // No system menu at all (no WS_SYSMENU). Fall back to the old behaviour.
            DefWindowProc(handle, WM_SYSCOMMAND, SC_KEYMENU, VK_SPACE);
            return 0;
        }

        // Called from the Alt+Space key-down, so both keys are still physically held. The
        // popup runs its own modal loop and takes the key-ups that follow as menu input,
        // which on 2026-09-14 chose an item before the menu was even painted: Alt+Space
        // alone minimised the window. Wait for the chord to be released first.
        var deadline = Environment.TickCount64 + (long)ReleaseWait.TotalMilliseconds;
        while (((GetAsyncKeyState(VK_MENU) | GetAsyncKeyState(VK_SPACE)) & 0x8000) != 0 && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(10);
        }

        // The key-ups are now sitting in this thread's queue. The popup's modal loop would
        // read them first, and a lone Alt key-up inside a menu means "leave the menu" — which
        // is why the popup closed before it was painted. Discard them.
        while (PeekMessage(out _, 0, WM_KEYFIRST, WM_KEYLAST, PM_REMOVE)) { }

        // Windows enables and disables Restore/Move/Size/Minimize/Maximize for the
        // window's current state when it handles WM_INITMENU for the system menu.
        SendMessage(handle, WM_INITMENU, menu, 0);

        // Where the keyboard-opened menu appears on a stock window: just inside the
        // frame, below the caption.
        var x = 0;
        var y = 0;
        if (GetWindowRect(handle, out var rect))
        {
            x = rect.Left + GetSystemMetrics(SM_CXFRAME);
            y = rect.Top + GetSystemMetrics(SM_CYCAPTION);
        }

        // TPM_RETURNCMD: the popup runs its own modal loop (arrow keys and mnemonics
        // included) and hands back the command rather than sending WM_COMMAND, which the
        // UI framework's window procedure would not understand.
        // A popup is only tracked for the foreground window; for any other owner it closes
        // before it is painted, and the key-ups that follow reach the window as bare
        // system-menu keystrokes. Seen on 2026-09-14 as "Alt+Space moved the window".
        if (GetForegroundWindow() != handle) SetForegroundWindow(handle);

        var started = Environment.TickCount64;
        var command = TrackPopupMenuEx(menu, TPM_LEFTBUTTON | TPM_RETURNCMD, x, y, handle, 0);
        var error = Marshal.GetLastWin32Error();
        // Documented follow-up: without it the next popup on the same window can misbehave.
        PostMessage(handle, WM_NULL, 0, 0);

        if (command == 0 && Environment.TickCount64 - started < 100)
        {
            LastError = $"closed at once (foreground={GetForegroundWindow() == handle}, win32={error})";
        }
        if (command != 0) PostMessage(handle, WM_SYSCOMMAND, command, 0);
        return command;
    }
}
