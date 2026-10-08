using System.IO;
using System.Runtime.InteropServices;

namespace FinderApp.Services;

public static class FastDirectoryScanner
{
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_HIDDEN = 0x00000002;
    private const uint FILE_ATTRIBUTE_SYSTEM = 0x00000004;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;

    private const int FIND_FIRST_EX_LARGE_FETCH = 2;
    private const int FindExInfoBasic = 1;
    private const int FindExSearchNameMatch = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindFirstFileExW(
        string lpFileName,
        int fInfoLevelId,
        out WIN32_FIND_DATAW lpFindFileData,
        int fSearchOp,
        IntPtr lpSearchFilter,
        int dwAdditionalFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr hFindFile);

    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    public delegate void EntryFoundCallback(string name, bool isDirectory);

    /// <summary>
    /// Scans a directory using low-level Win32 API.
    /// Completely avoids creating .NET FileInfo/DirectoryInfo objects to keep memory usage minimal.
    /// </summary>
    public static void ScanDirectory(string dirPath, EntryFoundCallback callback)
    {
        string normalized = dirPath;
        if (!normalized.StartsWith(@"\\?\") && Path.IsPathRooted(normalized))
        {
            normalized = @"\\?\" + normalized;
        }
        string searchPath = normalized.TrimEnd('\\', '/') + @"\*";
        IntPtr hFind = FindFirstFileExW(
            searchPath,
            FindExInfoBasic,
            out WIN32_FIND_DATAW findData,
            FindExSearchNameMatch,
            IntPtr.Zero,
            FIND_FIRST_EX_LARGE_FETCH);

        if (hFind == INVALID_HANDLE_VALUE)
        {
            return;
        }

        try
        {
            do
            {
                string name = findData.cFileName;

                // Skip "." and ".."
                if (name == "." || name == "..")
                    continue;

                // Skip hidden files that start with '.' (like .git, .vscode, .cache)
                if (name.Length > 0 && name[0] == '.')
                    continue;

                bool isReparsePoint = (findData.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
                bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;

                // Skip directory junctions / symlinks to avoid circular recursion
                if (isDir && isReparsePoint)
                    continue;

                string pooledName = StringPool.Intern(name);
                callback(pooledName, isDir);

            } while (FindNextFileW(hFind, out findData));
        }
        finally
        {
            FindClose(hFind);
        }
    }
}
