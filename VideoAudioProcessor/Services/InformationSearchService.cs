using System.Globalization;
using System.Text;

namespace VideoAudioProcessor.Services;

public sealed class InformationSearchService
{
    private readonly InformationIndexStorageService _storage = new();
    private InformationIndexDocument? _cachedIndex;

    public InformationIndexDocument EnsureIndex()
    {
        if (_cachedIndex != null)
        {
            return _cachedIndex;
        }

        _cachedIndex = _storage.Load() ?? new InformationIndexDocument
        {
            StoragePath = _storage.IndexPath
        };

        return _cachedIndex;
    }

    public IReadOnlyList<InformationSearchResult> Search(string? query, int top = 12)
    {
        var index = EnsureIndex();
        if (string.IsNullOrWhiteSpace(query))
        {
            return index.Chunks
                .Take(top)
                .Select(chunk => new InformationSearchResult(chunk, 0))
                .ToList();
        }

        var queryVector = BuildVector(query, index.Vocabulary, index.Idf);
        if (queryVector.Count == 0 || queryVector.All(value => value == 0))
        {
            return [];
        }

        return index.Chunks
            .Select(chunk => new InformationSearchResult(chunk, CosineSimilarity(queryVector, chunk.Vector)))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title)
            .Take(top)
            .ToList();
    }

    private static List<double> BuildVector(string source, IReadOnlyList<string> vocabulary, IReadOnlyList<double> idf)
    {
        if (vocabulary.Count == 0)
        {
            return [];
        }

        var tokens = Tokenize(source).ToList();
        if (tokens.Count == 0)
        {
            return Enumerable.Repeat(0d, vocabulary.Count).ToList();
        }

        var tokenCounts = tokens
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var vector = new List<double>(vocabulary.Count);
        var totalTokens = tokens.Count;

        for (var index = 0; index < vocabulary.Count; index++)
        {
            tokenCounts.TryGetValue(vocabulary[index], out var count);
            var tf = count / (double)totalTokens;
            vector.Add(tf * idf[index]);
        }

        return Normalize(vector);
    }

    private static List<double> Normalize(IReadOnlyList<double> values)
    {
        var sum = values.Sum(value => value * value);
        if (sum <= 0)
        {
            return values.ToList();
        }

        var norm = Math.Sqrt(sum);
        return values.Select(value => value / norm).ToList();
    }

    private static double CosineSimilarity(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var length = Math.Min(left.Count, right.Count);
        double score = 0;
        for (var index = 0; index < length; index++)
        {
            score += left[index] * right[index];
        }

        return score;
    }

    private static IEnumerable<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var buffer = new StringBuilder();
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Append(character);
                continue;
            }

            if (buffer.Length == 0)
            {
                continue;
            }

            yield return buffer.ToString();
            buffer.Clear();
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }
}

public sealed class InformationSearchResult
{
    private readonly InformationChunk _chunk;

    public InformationSearchResult(InformationChunk chunk, double score)
    {
        _chunk = chunk;
        Score = score;
    }

    public string Id => _chunk.Id;
    public string Title => _chunk.Title;
    public string Category => _chunk.Category;
    public string Location => _chunk.Location;
    public string ShortText => _chunk.ShortText;
    public string FullText => _chunk.FullText;
    public string ScoreText => Score <= 0 ? "Обзор" : Score.ToString("P0", CultureInfo.InvariantCulture);
    public double Score { get; }
}

public sealed class InformationIndexDocument
{
    public int Version { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public List<string> Vocabulary { get; set; } = [];
    public List<double> Idf { get; set; } = [];
    public List<InformationChunk> Chunks { get; set; } = [];
}

public sealed class InformationChunk
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ShortText { get; set; } = string.Empty;
    public string FullText { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public List<double> Vector { get; set; } = [];
}
