namespace FinderApp.Services;

/// <summary>
/// Ultra-lightweight direct-mapped string deduplication pool.
/// Reuses string references for frequent filenames (index.js, README.md, package.json, Thumbs.db, etc.)
/// Uses a fixed ~128 KB table, zero allocations, thread-safe without locks.
/// </summary>
public static class StringPool
{
    private const int TableSize = 16384;
    private const int Mask = TableSize - 1;
    private static readonly string?[] Table = new string?[TableSize];

    public static string Intern(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 64)
            return s;

        int hash = s.GetHashCode() & Mask;
        string? existing = Volatile.Read(ref Table[hash]);
        if (existing != null && string.Equals(existing, s, StringComparison.Ordinal))
        {
            return existing;
        }

        Volatile.Write(ref Table[hash], s);
        return s;
    }
}
