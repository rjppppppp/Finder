namespace FinderApp.Models;

public class SearchResponse
{
    public List<SearchCandidate> Candidates { get; set; } = new();
    public List<CategoryCount> Categories { get; set; } = new();
    public int TotalMatches => Candidates.Count;
}
