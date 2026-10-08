using System.IO;

namespace FinderApp.Services;

public static class FileCategoryHelper
{
    private static readonly Dictionary<string, (string Name, string Icon)> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        { ".png", ("Image", "🖼️") },
        { ".jpg", ("Image", "🖼️") },
        { ".jpeg", ("Image", "🖼️") },
        { ".webp", ("Image", "🖼️") },
        { ".gif", ("Image", "🖼️") },
        { ".bmp", ("Image", "🖼️") },
        { ".svg", ("Image", "🖼️") },
        { ".ico", ("Image", "🖼️") },
        { ".tiff", ("Image", "🖼️") },
        { ".psd", ("Image", "🖼️") },

        // Documents
        { ".pdf", ("Document", "📄") },
        { ".docx", ("Document", "📄") },
        { ".doc", ("Document", "📄") },
        { ".xlsx", ("Document", "📄") },
        { ".xls", ("Document", "📄") },
        { ".pptx", ("Document", "📄") },
        { ".ppt", ("Document", "📄") },
        { ".txt", ("Document", "📄") },
        { ".md", ("Document", "📄") },
        { ".csv", ("Document", "📄") },
        { ".rtf", ("Document", "📄") },

        // Apps / Executables
        { ".exe", ("App", "⚡") },
        { ".lnk", ("App", "⚡") },
        { ".bat", ("App", "⚡") },
        { ".cmd", ("App", "⚡") },
        { ".msi", ("App", "⚡") },
        { ".ps1", ("App", "⚡") },

        // Code
        { ".cs", ("Code", "💻") },
        { ".js", ("Code", "💻") },
        { ".ts", ("Code", "💻") },
        { ".py", ("Code", "💻") },
        { ".html", ("Code", "💻") },
        { ".css", ("Code", "💻") },
        { ".json", ("Code", "💻") },
        { ".xml", ("Code", "💻") },
        { ".cpp", ("Code", "💻") },
        { ".c", ("Code", "💻") },
        { ".java", ("Code", "💻") },
        { ".rs", ("Code", "💻") },
        { ".go", ("Code", "💻") },
        { ".sql", ("Code", "💻") },
        { ".php", ("Code", "💻") },

        // Video
        { ".mp4", ("Video", "🎬") },
        { ".mkv", ("Video", "🎬") },
        { ".avi", ("Video", "🎬") },
        { ".mov", ("Video", "🎬") },
        { ".wmv", ("Video", "🎬") },
        { ".webm", ("Video", "🎬") },

        // Audio
        { ".mp3", ("Audio", "🎵") },
        { ".wav", ("Audio", "🎵") },
        { ".flac", ("Audio", "🎵") },
        { ".aac", ("Audio", "🎵") },
        { ".ogg", ("Audio", "🎵") },
        { ".m4a", ("Audio", "🎵") },

        // Archives
        { ".zip", ("Archive", "📦") },
        { ".rar", ("Archive", "📦") },
        { ".7z", ("Archive", "📦") },
        { ".tar", ("Archive", "📦") },
        { ".gz", ("Archive", "📦") },
        { ".iso", ("Archive", "📦") }
    };

    public static (string Category, string Icon) GetCategory(string fileName, bool isDirectory)
    {
        if (isDirectory)
        {
            return ("Folder", "📁");
        }

        int dot = fileName.LastIndexOf('.');
        if (dot < 0 || dot == fileName.Length - 1)
        {
            return ("Other", "📄");
        }

        string ext = fileName.Substring(dot);
        string pooledExt = StringPool.Intern(ext);
        if (ExtensionMap.TryGetValue(pooledExt, out var cat))
        {
            return cat;
        }

        return ("Other", "📄");
    }
}
