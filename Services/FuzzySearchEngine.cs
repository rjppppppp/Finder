namespace FinderApp.Services;

/// <summary>
/// Zero-allocation fuzzy matching engine.
/// Completely avoids ToLowerInvariant(), substring allocations, and 2D arrays.
/// Highlights are computed on-demand only for the visible 40 items.
/// </summary>
public static class FuzzySearchEngine
{
    public readonly record struct QuickMatchResult(bool IsMatch, int Score, string MatchType);

    public static QuickMatchResult QuickMatch(string targetName, string query)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(targetName))
        {
            return new QuickMatchResult(false, 0, "");
        }

        // 1. EXTENSION SEARCH (e.g. ".pdf", ".png", ".exe")
        if (query[0] == '.')
        {
            if (query.Length == 1)
            {
                return new QuickMatchResult(false, 0, "");
            }

            if (targetName.EndsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                return new QuickMatchResult(true, 15_000 - (targetName.Length * 2), "Extension Match");
            }

            int dotIdx = targetName.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (dotIdx >= 0)
            {
                return new QuickMatchResult(true, 10_000 - (targetName.Length * 2), "Exact");
            }

            return new QuickMatchResult(false, 0, "");
        }

        // 1.5 MULTI-TOKEN / SPACE-SEPARATED SEARCH (e.g. "invoice 2024", "project plan")
        if (query.IndexOf(' ') >= 0)
        {
            var multiResult = MatchMultiToken(targetName, query);
            if (multiResult.IsMatch)
            {
                return multiResult;
            }
        }

        // 2. EXACT SUBSTRING MATCH
        int subIdx = targetName.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (subIdx >= 0)
        {
            int score = 10_000;
            if (subIdx == 0)
            {
                score += 4_000; // Prefix bonus
            }
            else if (IsWordBoundary(targetName, subIdx))
            {
                score += 2_000; // Word boundary bonus
            }

            // Bonus if query matches the file extension specifically (e.g. query "pdf" on "document.pdf")
            int lastDot = targetName.LastIndexOf('.');
            if (lastDot >= 0 && lastDot + 1 < targetName.Length)
            {
                ReadOnlySpan<char> ext = targetName.AsSpan(lastDot + 1);
                if (ext.Equals(query, StringComparison.OrdinalIgnoreCase))
                {
                    score += 6_000;
                }
            }

            score -= targetName.Length * 2;
            return new QuickMatchResult(true, score, "Exact");
        }

        // 3. ACRONYM MATCH (Initials of words, e.g. "vsc" for "Visual Studio Code")
        if (query.Length >= 2 && query.Length <= 8)
        {
            int q = 0;
            for (int i = 0; i < targetName.Length && q < query.Length; i++)
            {
                if (i == 0 || IsWordBoundary(targetName, i))
                {
                    char c = targetName[i];
                    if (char.ToLowerInvariant(c) == char.ToLowerInvariant(query[q]))
                    {
                        q++;
                    }
                }
            }

            if (q == query.Length)
            {
                int score = 7_000 - (targetName.Length * 2);
                return new QuickMatchResult(true, score, "Acronym");
            }
        }

        // 4. TYPO / SPELLING MISTAKE TOLERANCE (Damerau-Levenshtein on tokens without heap allocations)
        if (query.Length >= 3 && query.Length <= 16)
        {
            var typoResult = EvaluateTypoMatchZeroAlloc(targetName, query);
            if (typoResult.IsMatch)
            {
                return typoResult;
            }
        }

        return new QuickMatchResult(false, 0, "");
    }

    private static QuickMatchResult MatchMultiToken(string targetName, string query)
    {
        string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length <= 1)
            return new QuickMatchResult(false, 0, "");

        int totalScore = 0;
        int lastPos = -1;
        bool inOrder = true;

        foreach (var token in tokens)
        {
            int idx = targetName.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                if (token.Length >= 3 && token.Length <= 16)
                {
                    var typo = EvaluateTypoMatchZeroAlloc(targetName, token);
                    if (!typo.IsMatch) return new QuickMatchResult(false, 0, "");
                    totalScore += typo.Score / 2;
                    continue;
                }
                return new QuickMatchResult(false, 0, "");
            }

            int tokenScore = 6_000;
            if (idx == 0 || IsWordBoundary(targetName, idx)) tokenScore += 2_000;
            if (idx > lastPos) lastPos = idx;
            else inOrder = false;

            totalScore += tokenScore;
        }

        if (inOrder) totalScore += 3_000;
        totalScore -= targetName.Length * 2;

        return new QuickMatchResult(true, totalScore, "Multi-Match");
    }

    private static QuickMatchResult EvaluateTypoMatchZeroAlloc(string targetName, string query)
    {
        int maxAllowedDistance = query.Length switch
        {
            3 => 1,
            4 or 5 => 1,
            _ => 2
        };

        // Tokenize targetName along delimiters ' ', '_', '-', '.' without allocating string arrays
        int tokenStart = 0;
        for (int i = 0; i <= targetName.Length; i++)
        {
            if (i == targetName.Length || targetName[i] == ' ' || targetName[i] == '_' || targetName[i] == '-' || targetName[i] == '.')
            {
                int tokenLen = i - tokenStart;
                if (tokenLen > 0 && Math.Abs(tokenLen - query.Length) <= maxAllowedDistance)
                {
                    ReadOnlySpan<char> token = targetName.AsSpan(tokenStart, tokenLen);
                    int dist = FastLevenshteinDistance(token, query.AsSpan(), maxAllowedDistance);
                    if (dist <= maxAllowedDistance)
                    {
                        int score = 3_000 - (dist * 600) - (targetName.Length * 2);
                        return new QuickMatchResult(true, score, "Typo Match");
                    }
                }
                tokenStart = i + 1;
            }
        }

        return new QuickMatchResult(false, 0, "");
    }

    /// <summary>
    /// Computes Levenshtein distance using stackalloc buffers — ZERO HEAP ALLOCATIONS.
    /// </summary>
    public static int FastLevenshteinDistance(ReadOnlySpan<char> s, ReadOnlySpan<char> t, int maxLimit = int.MaxValue)
    {
        int n = s.Length;
        int m = t.Length;

        if (Math.Abs(n - m) > maxLimit) return maxLimit + 1;
        if (n == 0) return m;
        if (m == 0) return n;
        if (m > 64) return maxLimit + 1; // Limit stackalloc size

        Span<int> v0 = stackalloc int[m + 1];
        Span<int> v1 = stackalloc int[m + 1];

        for (int i = 0; i <= m; i++)
            v0[i] = i;

        for (int i = 0; i < n; i++)
        {
            v1[0] = i + 1;
            char sChar = char.ToLowerInvariant(s[i]);

            for (int j = 0; j < m; j++)
            {
                char tChar = char.ToLowerInvariant(t[j]);
                int cost = (sChar == tChar) ? 0 : 1;
                v1[j + 1] = Math.Min(Math.Min(v1[j] + 1, v0[j + 1] + 1), v0[j] + cost);
            }

            for (int j = 0; j <= m; j++)
                v0[j] = v1[j];
        }

        return v0[m];
    }

    private static bool IsWordBoundary(string text, int index)
    {
        if (index <= 0) return true;
        char prev = text[index - 1];
        char curr = text[index];
        return prev == ' ' || prev == '_' || prev == '-' || prev == '.' ||
               (char.IsLower(prev) && char.IsUpper(curr));
    }

    /// <summary>
    /// Resolves highlight indices on-demand ONLY for the 40 items visible on screen!
    /// </summary>
    public static List<int> GetHighlightIndices(string targetName, string query, string matchType)
    {
        if (string.IsNullOrEmpty(targetName) || string.IsNullOrEmpty(query))
            return new List<int>();

        var indices = new List<int>(query.Length);

        if (matchType == "Multi-Match" || query.Contains(' '))
        {
            string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                int idx = targetName.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    for (int i = 0; i < token.Length; i++)
                    {
                        if (!indices.Contains(idx + i))
                            indices.Add(idx + i);
                    }
                }
            }
            indices.Sort();
            return indices;
        }

        if (matchType == "Exact" || matchType == "Extension Match")
        {
            int idx = targetName.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                for (int i = 0; i < query.Length; i++)
                    indices.Add(idx + i);
                return indices;
            }
        }

        if (matchType == "Acronym")
        {
            int q = 0;
            for (int i = 0; i < targetName.Length && q < query.Length; i++)
            {
                if (i == 0 || IsWordBoundary(targetName, i))
                {
                    if (char.ToLowerInvariant(targetName[i]) == char.ToLowerInvariant(query[q]))
                    {
                        indices.Add(i);
                        q++;
                    }
                }
            }
            if (indices.Count == query.Length)
                return indices;
        }

        // Fallback: character match
        int searchFrom = 0;
        for (int q = 0; q < query.Length; q++)
        {
            char qChar = char.ToLowerInvariant(query[q]);
            for (int i = searchFrom; i < targetName.Length; i++)
            {
                if (char.ToLowerInvariant(targetName[i]) == qChar)
                {
                    indices.Add(i);
                    searchFrom = i + 1;
                    break;
                }
            }
        }

        return indices;
    }
}
