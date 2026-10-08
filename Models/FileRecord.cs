namespace FinderApp.Models;

/// <summary>
/// Ultra-lean in-memory representation.
/// No file size or timestamps stored here to save maximum RAM.
/// Size is resolved on-demand only for the top 30 visible results.
/// </summary>
public readonly struct FileRecord
{
    public int DirIndex { get; }
    public string Name { get; }
    public bool IsDirectory { get; }

    public FileRecord(int dirIndex, string name, bool isDirectory)
    {
        DirIndex = dirIndex;
        Name = name;
        IsDirectory = isDirectory;
    }
}
