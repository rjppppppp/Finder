using System.Windows;
using System.Windows.Media;

namespace FinderApp.Models;

public class SearchResultItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string DirectoryPath { get; init; }
    public bool IsDirectory { get; init; }
    public string SizeText { get; set; } = "";
    public string DateText { get; set; } = "";
    public uint ModifiedTime { get; set; } = 0;
    public List<int> HighlightIndices { get; init; } = new();
    public int Score { get; init; }
    public string MatchType { get; init; } = "";
    public ImageSource? Icon { get; set; }
    public string Category { get; set; } = "Other";
    public string CategoryIcon { get; set; } = "📄";

    // Clean UI presentation properties
    public bool HasSpecialMatch => MatchType == "Typo Match" || MatchType == "Acronym";
    public string MatchDisplayTag => MatchType == "Typo Match" ? "~ Typo" : (MatchType == "Acronym" ? "⚡ Acronym" : "");
    public Visibility MatchTagVisibility => HasSpecialMatch ? Visibility.Visible : Visibility.Collapsed;
}
