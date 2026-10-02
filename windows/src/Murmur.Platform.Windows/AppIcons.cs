using System.Diagnostics;
using System.Runtime.InteropServices;
using Murmur.Abstractions;

namespace Murmur.Platform.Windows;

/// <summary>
/// Reads the icon out of a running application's executable, the way Explorer shows it.
/// </summary>
/// <remarks>
/// The process is found by the name the history records, its image path read with
/// <c>QueryFullProcessImageName</c> (limited rights, so it works for packaged and elevated
/// apps too), and the icon drawn into a 32-bit DIB at the size asked for. Store apps such as
/// WhatsApp ship an exe with no icon in it, so for those the logo named in the package's
/// AppxManifest.xml is loaded with GDI+ instead. Anything that fails returns null and the
/// history shows a tinted initial instead.
/// </remarks>
public sealed class AppIcons : IAppIcons
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int DI_NORMAL = 0x0003;

    /// <inheritdoc />
    public AppIcon? Find(string app, int size)
    {
        try
        {
            var path = ImagePath(app);
            if (path is null) return null;
            return Extract(path, size) ?? (PackageLogo.Find(path, size) is { } logo ? FromImageFile(logo, size) : null);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or ExternalException or ArgumentException or IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static AppIcon? FromImageFile(string file, int size)
    {
        var input = new GdiplusStartupInput { GdiplusVersion = 1 };
        if (GdiplusStartup(out var token, ref input, 0) != 0) return null;
        try
        {
            if (GdipCreateBitmapFromFile(file, out var bitmap) != 0) return null;
            try
            {
                if (GdipCreateHICONFromBitmap(bitmap, out var icon) != 0 || icon == 0) return null;
                return Draw(icon, size);
            }
            finally
            {
                _ = GdipDisposeImage(bitmap);
            }
        }
        finally
        {
            GdiplusShutdown(token);
        }
    }

    private static string? ImagePath(string app)
    {
        foreach (var process in Process.GetProcessesByName(app))
        {
            using (process)
            {
                var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)process.Id);
                if (handle == 0) continue;
                try
                {
                    var buffer = new char[1024];
                    var length = buffer.Length;
                    if (QueryFullProcessImageName(handle, 0, buffer, ref length) && length > 0) return new string(buffer, 0, length);
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
        }
        return null;
    }

    private static AppIcon? Extract(string path, int size)
    {
        var sizes = (uint)(size & 0xFFFF) | (16u << 16);
        if (SHDefExtractIconW(path, 0, 0, out var large, out var small, sizes) != 0 || large == 0)
        {
            if (small != 0) DestroyIcon(small);
            return null;
        }
        if (small != 0) DestroyIcon(small);
        return Draw(large, size);
    }

    /// <summary>Draws <paramref name="large"/> into BGRA pixels and destroys it.</summary>
    private static AppIcon? Draw(nint large, int size)
    {
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        try
        {
            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size,   // top row first
                biPlanes = 1,
                biBitCount = 32,
            };
            var bitmap = CreateDIBSection(memory, ref header, 0, out var bits, 0, 0);
            if (bitmap == 0) return null;
            try
            {
                var previous = SelectObject(memory, bitmap);
                DrawIconEx(memory, 0, 0, large, size, size, 0, 0, DI_NORMAL);
                GdiFlush();
                var pixels = new byte[size * size * 4];
                Marshal.Copy(bits, pixels, 0, pixels.Length);
                SelectObject(memory, previous);
                return HasAlpha(pixels) ? new AppIcon(size, size, pixels) : null;
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        finally
        {
            DeleteDC(memory);
            _ = ReleaseDC(0, screen);
            DestroyIcon(large);
        }
    }

    /// <summary>An old icon drawn with a mask only comes back with no alpha; it would show as a black square.</summary>
    private static bool HasAlpha(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) return true;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [DllImport("gdiplus.dll")]
    private static extern int GdiplusStartup(out nint token, ref GdiplusStartupInput input, nint output);

    [DllImport("gdiplus.dll")]
    private static extern void GdiplusShutdown(nint token);

    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    private static extern int GdipCreateBitmapFromFile(string file, out nint bitmap);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateHICONFromBitmap(nint bitmap, out nint icon);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDisposeImage(nint image);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, [Out] char[] name, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIconW(string file, int index, uint flags, out nint large, out nint small, uint sizes);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, int step, nint brush, int flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER header, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GdiFlush();
}
