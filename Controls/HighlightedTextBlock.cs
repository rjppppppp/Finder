using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FinderApp.Controls;

public class HighlightedTextBlock : TextBlock
{
    public static readonly DependencyProperty FullTextProperty =
        DependencyProperty.Register(
            nameof(FullText),
            typeof(string),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(string.Empty, OnPropertyChanged));

    public static readonly DependencyProperty HighlightIndicesProperty =
        DependencyProperty.Register(
            nameof(HighlightIndices),
            typeof(List<int>),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(null, OnPropertyChanged));

    public static readonly DependencyProperty HighlightBrushProperty =
        DependencyProperty.Register(
            nameof(HighlightBrush),
            typeof(Brush),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(56, 189, 248)), OnPropertyChanged)); // Sky-400

    public static readonly DependencyProperty NormalBrushProperty =
        DependencyProperty.Register(
            nameof(NormalBrush),
            typeof(Brush),
            typeof(HighlightedTextBlock),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(241, 245, 249)), OnPropertyChanged)); // Slate-100

    public string FullText
    {
        get => (string)GetValue(FullTextProperty);
        set => SetValue(FullTextProperty, value);
    }

    public List<int>? HighlightIndices
    {
        get => (List<int>?)GetValue(HighlightIndicesProperty);
        set => SetValue(HighlightIndicesProperty, value);
    }

    public Brush HighlightBrush
    {
        get => (Brush)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    public Brush NormalBrush
    {
        get => (Brush)GetValue(NormalBrushProperty);
        set => SetValue(NormalBrushProperty, value);
    }

    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HighlightedTextBlock htb)
        {
            htb.UpdateInlines();
        }
    }

    private void UpdateInlines()
    {
        Inlines.Clear();
        string text = FullText;
        if (string.IsNullOrEmpty(text)) return;

        var indices = HighlightIndices;
        if (indices == null || indices.Count == 0)
        {
            Inlines.Add(new Run(text) { Foreground = NormalBrush });
            return;
        }

        int ptr = 0;
        int start = 0;
        while (ptr < indices.Count && indices[ptr] < 0) ptr++;
        bool isCurrentHighlighted = (ptr < indices.Count && indices[ptr] == 0);

        for (int i = 1; i < text.Length; i++)
        {
            while (ptr < indices.Count && indices[ptr] < i) ptr++;
            bool isCharHighlighted = (ptr < indices.Count && indices[ptr] == i);

            if (isCharHighlighted != isCurrentHighlighted)
            {
                // Push run for [start..i]
                string segment = text.Substring(start, i - start);
                var run = new Run(segment)
                {
                    Foreground = isCurrentHighlighted ? HighlightBrush : NormalBrush,
                    FontWeight = isCurrentHighlighted ? FontWeights.Bold : FontWeights.Normal
                };
                Inlines.Add(run);

                start = i;
                isCurrentHighlighted = isCharHighlighted;
            }
        }

        // Push trailing segment
        if (start < text.Length)
        {
            string segment = text.Substring(start, text.Length - start);
            var run = new Run(segment)
            {
                Foreground = isCurrentHighlighted ? HighlightBrush : NormalBrush,
                FontWeight = isCurrentHighlighted ? FontWeights.Bold : FontWeights.Normal
            };
            Inlines.Add(run);
        }
    }
}
