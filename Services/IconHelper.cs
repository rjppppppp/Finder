using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FinderApp.Services;

public static class IconHelper
{
    private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new();
    private static ImageSource? _folderIcon;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    public static ImageSource? GetIcon(string fullPath, bool isDirectory)
    {
        if (isDirectory)
        {
            if (_folderIcon != null) return _folderIcon;
            _folderIcon = ExtractIcon("dummy_folder", true);
            return _folderIcon;
        }

        string ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".unknown";

        // For .exe or .lnk files, icon might be file-specific; for general files, extension icon is enough
        if (ext == ".exe" || ext == ".lnk")
        {
            if (File.Exists(fullPath))
            {
                return ExtractIcon(fullPath, false, useActualFile: true);
            }
        }

        return _cache.GetOrAdd(ext, k => ExtractIcon(k, false));
    }

    private static ImageSource? ExtractIcon(string pathOrExt, bool isDir, bool useActualFile = false)
    {
        var shinfo = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON;
        uint attrs = isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

        if (!useActualFile)
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        IntPtr result = SHGetFileInfo(pathOrExt, attrs, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
        if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                shinfo.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            bitmap.Freeze(); // Make cross-thread safe
            return bitmap;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(shinfo.hIcon);
        }
    }
}
