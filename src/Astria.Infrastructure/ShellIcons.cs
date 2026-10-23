using System.Runtime.InteropServices;
namespace Astria.Infrastructure;

public static class ShellIcons
{
    public static byte[]? Read(string path, int size = 64)
    {
        path = InstalledPath.Resolve(path);
        if (path.StartsWith("::{", StringComparison.Ordinal)) path = "shell:" + path;
        FileInfoNative info; nint pidl = 0, icon = 0, dc = 0, dib = 0, old = 0;
        try
        {
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                if (SHParseDisplayName(path[6..], 0, out pidl, 0, out _) != 0) return null;
                if (SHGetFileInfo(pidl, 0, out info, (uint)Marshal.SizeOf<FileInfoNative>(), 0x100 | 8) == 0) return null;
            }
            else if (SHGetFileInfo(path, 0, out info, (uint)Marshal.SizeOf<FileInfoNative>(), 0x100) == 0) return null;
            icon = info.Icon; if (icon == 0) return null;
            dc = CreateCompatibleDC(0); var header = new BitmapInfo { Size = 40, Width = size, Height = -size, Planes = 1, BitCount = 32 };
            dib = CreateDIBSection(dc, ref header, 0, out var pixels, 0, 0); if (dib == 0) return null;
            old = SelectObject(dc, dib); Marshal.Copy(new byte[size * size * 4], 0, pixels, size * size * 4);
            DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, 3);
            var bytes = new byte[size * size * 4]; Marshal.Copy(pixels, bytes, 0, bytes.Length);
            // Some classic icons use a mask without alpha; reconstruct opaque colored pixels.
            if (!bytes.Where((_, i) => i % 4 == 3).Any(a => a != 0)) for (var i = 0; i < bytes.Length; i += 4) if (bytes[i] != 0 || bytes[i + 1] != 0 || bytes[i + 2] != 0) bytes[i + 3] = 255;
            return bytes;
        }
        finally
        {
            if (old != 0) SelectObject(dc, old); if (dib != 0) DeleteObject(dib); if (dc != 0) DeleteDC(dc);
            if (icon != 0) DestroyIcon(icon); if (pidl != 0) Marshal.FreeCoTaskMem(pidl);
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfoNative
    { public nint Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Display; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Type; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XPels, YPels; public uint Used, Important; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, out FileInfoNative info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(nint path, uint attributes, out FileInfoNative info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string path, nint context, out nint pidl, uint attributes, out uint result);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
