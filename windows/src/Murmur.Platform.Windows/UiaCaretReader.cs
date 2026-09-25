using System.Runtime.InteropServices;

namespace Murmur.Platform.Windows;

/// <summary>
/// Reads the few characters before the caret in the focused control, through UI Automation.
/// </summary>
/// <remarks>
/// <para>
/// Read only. UI Automation cannot insert text (see <see cref="SendInputTextInjector"/>),
/// but <c>TextPattern</c> can say what is around the caret: take the selection, collapse it
/// to its start and stretch that back a few characters.
/// </para>
/// <para>
/// Only controls that expose <c>TextPattern</c> answer: Win32 edit boxes, WPF, Word and the
/// new Notepad always; Chrome, Edge and Electron once their accessibility tree is on, which
/// a UI Automation client asking is enough to start. Anything else returns null.
/// </para>
/// <para>
/// One read runs at a time. An app that stops answering leaves its call parked on a pool
/// thread, and every later dictation would park another; while one is stuck they wait 100 ms
/// and return null.
/// </para>
/// </remarks>
internal static class UiaCaretReader
{
    private const int TextPatternId = 10014;
    private const int EndpointStart = 0;
    private const int EndpointEnd = 1;
    private const int UnitCharacter = 0;

    private const uint ClsctxInprocServer = 1;

    private static readonly Lazy<IUIAutomation> Automation = new(Create);

    private static int _busy;

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);

    private static IUIAutomation Create()
    {
        var clsid = new Guid("ff48dba4-60ef-4201-aa87-54103eef594e");
        var iid = new Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, ClsctxInprocServer, ref iid, out var pointer));
        try { return (IUIAutomation)Marshal.GetObjectForIUnknown(pointer); }
        finally { Marshal.Release(pointer); }
    }

    /// <summary>
    /// Takes the one-read-at-a-time slot. A read already running is normally a few
    /// milliseconds from done, and the key-down read must not come back empty just because
    /// the edit watcher happened to be mid-read; a stuck app still costs only this wait.
    /// </summary>
    private static bool Enter()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0) return true;
            Thread.Sleep(5);
        }
        return false;
    }

    /// <summary>
    /// The text from <paramref name="before"/> characters before the caret to
    /// <paramref name="after"/> characters after it, or null.
    /// </summary>
    public static string? ReadAround(int before, int after)
    {
        if (!Enter()) return null;
        try
        {
            var focused = Automation.Value.GetFocusedElement();
            if (focused.GetCurrentPattern(TextPatternId) is not IUIAutomationTextPattern pattern) return null;

            var selection = pattern.GetSelection();
            if (selection.Length == 0) return null;

            var range = selection.GetElement(0).Clone();
            range.MoveEndpointByUnit(EndpointStart, UnitCharacter, -before);
            range.MoveEndpointByUnit(EndpointEnd, UnitCharacter, after);
            return range.GetText(-1);
        }
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>The text before the caret, at most <paramref name="maxLength"/> characters, or null.</summary>
    public static string? Read(int maxLength)
    {
        if (!Enter()) return null;
        try
        {
            var focused = Automation.Value.GetFocusedElement();
            if (focused.GetCurrentPattern(TextPatternId) is not IUIAutomationTextPattern pattern) return null;

            var selection = pattern.GetSelection();
            if (selection.Length == 0) return null;

            var range = selection.GetElement(0).Clone();
            range.MoveEndpointByRange(EndpointEnd, range, EndpointStart);
            range.MoveEndpointByUnit(EndpointStart, UnitCharacter, -maxLength);
            return range.GetText(maxLength);
        }
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    // Only the members called are declared with their real signatures; the rest are
    // placeholders that keep the vtable slots in the order UIAutomationClient.h has them.

    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements();
        void CompareRuntimeIds();
        void GetRootElement();
        void ElementFromHandle();
        void ElementFromPoint();
        IUIAutomationElement GetFocusedElement();
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        void FindAll();
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();
        void GetCurrentPropertyValue();
        void GetCurrentPropertyValueEx();
        void GetCachedPropertyValue();
        void GetCachedPropertyValueEx();
        void GetCurrentPatternAs();
        void GetCachedPatternAs();
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetCurrentPattern(int patternId);
    }

    [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        void RangeFromPoint();
        void RangeFromChild();
        IUIAutomationTextRangeArray GetSelection();
    }

    [ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRangeArray
    {
        int Length { get; }
        IUIAutomationTextRange GetElement(int index);
    }

    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        IUIAutomationTextRange Clone();
        void Compare();
        void CompareEndpoints();
        void ExpandToEnclosingUnit();
        void FindAttribute();
        void FindText();
        void GetAttributeValue();
        void GetBoundingRectangles();
        void GetEnclosingElement();
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetText(int maxLength);
        void Move();
        int MoveEndpointByUnit(int endpoint, int unit, int count);
        void MoveEndpointByRange(int endpoint, IUIAutomationTextRange target, int targetEndpoint);
    }
}
