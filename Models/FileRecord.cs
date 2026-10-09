namespace FinderApp.Models;

/// <summary>
/// Ultra-lean in-memory representation.
/// ModifiedTime stores Unix epoch seconds (uint = 4 bytes).
/// Placed right between DirIndex (4 bytes) and Name pointer (8 bytes on 64-bit),
/// it occupies the 4-byte padding hole with ZERO extra memory footprint.
/// </summary>
public readonly struct FileRecord
{
    public int DirIndex { get; }
    public uint ModifiedTime { get; }
    public string Name { get; }
    public bool IsDirectory { get; }

    public FileRecord(int dirIndex, string name, bool isDirectory, uint modifiedTime = 0)
    {
        DirIndex = dirIndex;
        ModifiedTime = modifiedTime;
        Name = name;
        IsDirectory = isDirectory;
    }
}
