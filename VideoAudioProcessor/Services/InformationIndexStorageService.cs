using System.Globalization;
using System.IO;
using System.Text;

namespace VideoAudioProcessor.Services;

public sealed class InformationIndexStorageService
{
    public string IndexPath => Path.Combine(GetIndexDirectory(), "semantic-search-index.semidx");

    public InformationIndexDocument? Load()
    {
        try
        {
            if (!File.Exists(IndexPath))
            {
                return null;
            }

            var lines = File.ReadAllLines(IndexPath, Encoding.UTF8);
            if (lines.Length < 2 || lines[0] != "VAPSEMIDX")
            {
                return null;
            }

            var document = new InformationIndexDocument
            {
                StoragePath = IndexPath
            };

            var index = 1;
            document.Version = int.Parse(lines[index++], CultureInfo.InvariantCulture);
            document.GeneratedAtUtc = new DateTime(long.Parse(lines[index++], CultureInfo.InvariantCulture), DateTimeKind.Utc);

            var vocabularyCount = int.Parse(lines[index++], CultureInfo.InvariantCulture);
            for (var i = 0; i < vocabularyCount; i++)
            {
                var parts = SplitLine(lines[index++], 2);
                document.Vocabulary.Add(Decode(parts[0]));
                document.Idf.Add(double.Parse(parts[1], CultureInfo.InvariantCulture));
            }

            var chunkCount = int.Parse(lines[index++], CultureInfo.InvariantCulture);
            for (var i = 0; i < chunkCount; i++)
            {
                var parts = SplitLine(lines[index++], 7);
                document.Chunks.Add(new InformationChunk
                {
                    Id = Decode(parts[0]),
                    Category = Decode(parts[1]),
                    Title = Decode(parts[2]),
                    Location = Decode(parts[3]),
                    ShortText = Decode(parts[4]),
                    FullText = Decode(parts[5]),
                    SearchText = Decode(parts[6]),
                    Vector = ParseVector(lines[index++])
                });
            }

            return document;
        }
        catch
        {
            return null;
        }
    }

    public void Save(InformationIndexDocument document)
    {
        Directory.CreateDirectory(GetIndexDirectory());

        var builder = new StringBuilder();
        builder.AppendLine("VAPSEMIDX");
        builder.AppendLine(document.Version.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(document.GeneratedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine(document.Vocabulary.Count.ToString(CultureInfo.InvariantCulture));

        for (var i = 0; i < document.Vocabulary.Count; i++)
        {
            builder.Append(Encode(document.Vocabulary[i]));
            builder.Append('\t');
            builder.AppendLine(document.Idf[i].ToString("R", CultureInfo.InvariantCulture));
        }

        builder.AppendLine(document.Chunks.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var chunk in document.Chunks)
        {
            builder.Append(Encode(chunk.Id));
            builder.Append('\t');
            builder.Append(Encode(chunk.Category));
            builder.Append('\t');
            builder.Append(Encode(chunk.Title));
            builder.Append('\t');
            builder.Append(Encode(chunk.Location));
            builder.Append('\t');
            builder.Append(Encode(chunk.ShortText));
            builder.Append('\t');
            builder.Append(Encode(chunk.FullText));
            builder.Append('\t');
            builder.AppendLine(Encode(chunk.SearchText));
            builder.AppendLine(SerializeVector(chunk.Vector));
        }

        File.WriteAllText(IndexPath, builder.ToString(), Encoding.UTF8);
    }

    private static string GetIndexDirectory()
    {
        var current = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(current);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VideoAudioProcessor.sln")))
            {
                return Path.Combine(directory.FullName, "Tools", "generated");
            }

            directory = directory.Parent;
        }

        return Path.Combine(current, "generated");
    }

    private static string SerializeVector(IEnumerable<double> vector)
    {
        return string.Join(",", vector.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
    }

    private static List<double> ParseVector(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return [];
        }

        return line.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
            .ToList();
    }

    private static string[] SplitLine(string line, int expectedCount)
    {
        var parts = line.Split('\t');
        if (parts.Length != expectedCount)
        {
            throw new InvalidDataException("Invalid semantic index line.");
        }

        return parts;
    }

    private static string Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static string Decode(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}
