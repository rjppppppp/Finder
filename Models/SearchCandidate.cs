namespace FinderApp.Models;

/// <summary>
/// Ultra-compact value struct for holding search match results in-memory.
/// Takes only ~40 bytes in a contiguous array without creating individual heap objects.
/// Full path and UI elements are resolved on-demand only for the visible 40 items.
/// </summary>
public readonly struct SearchCandidate
{
    public readonly int DirIndex;
    public readonly uint ModifiedTime;
    public readonly string Name;
    public readonly bool IsDirectory;
    public readonly int Score;
    public readonly string MatchType;
    public readonly string Category;
    public readonly string CategoryIcon;

    public SearchCandidate(
        int dirIndex,
        uint modifiedTime,
        string name,
        bool isDirectory,
        int score,
        string matchType,
        string category,
        string categoryIcon)
    {
        DirIndex = dirIndex;
        ModifiedTime = modifiedTime;
        Name = name;
        IsDirectory = isDirectory;
        Score = score;
        MatchType = matchType;
        Category = category;
        CategoryIcon = categoryIcon;
    }
}
