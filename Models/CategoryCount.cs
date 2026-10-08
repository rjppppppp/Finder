using System.Windows.Media;

namespace FinderApp.Models;

public class CategoryCount
{
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public int Count { get; set; }
    public bool IsSelected { get; set; }
    public string DisplayText => $"{Icon} {Name} ({Count:N0})";

    public Brush Background => IsSelected
        ? new SolidColorBrush(Color.FromArgb(230, 30, 58, 102)) // Highlighted blue tint
        : new SolidColorBrush(Color.FromArgb(180, 24, 28, 40));

    public Brush BorderBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) // Sky-400
        : new SolidColorBrush(Color.FromRgb(40, 47, 66));

    public Brush Foreground => IsSelected
        ? new SolidColorBrush(Color.FromRgb(255, 255, 255))
        : new SolidColorBrush(Color.FromRgb(148, 163, 184));
}
