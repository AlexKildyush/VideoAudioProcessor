using System.Globalization;
using VideoAudioProcessor.Services;

namespace VideoAudioProcessor.IntegrationTests;

internal sealed class TestWorkspace : IAsyncDisposable
{
    public TestWorkspace()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"vap_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
        Storage = new TrackStorageService(RootPath);
        Runner = new FfmpegCommandRunner();
        Probe = new MediaProbeService(Runner);
        Processing = new MediaProcessingService(Storage, Runner);
        BatchRunner = new BatchQueueRunner(Runner);
        ProjectRenderer = new ProjectRenderService(Storage, Probe, Runner);
    }

    public string RootPath { get; }
    public TrackStorageService Storage { get; }
    public FfmpegCommandRunner Runner { get; }
    public MediaProbeService Probe { get; }
    public MediaProcessingService Processing { get; }
    public BatchQueueRunner BatchRunner { get; }
    public ProjectRenderService ProjectRenderer { get; }

    public async Task<MediaSampleSet> CreateMediaSetAsync()
    {
        return await TestMediaFactory.CreateAsync(this);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, true);
            }
        }
        catch
        {
        }

        await Task.CompletedTask;
    }
}

internal sealed record MediaSampleSet(
    string VideoWithAudioPath,
    string VideoWithoutAudioPath,
    string MergeVideoPath,
    string AudioPath,
    string ImageOnePath,
    string ImageTwoPath,
    string SubtitlePath);

internal sealed record MediaInfo(
    string Path,
    double Duration,
    int Width,
    int Height,
    double Fps,
    bool HasVideo,
    bool HasAudio,
    bool HasSubtitle,
    string FormatName);

internal static class MediaInfoReader
{
    public static async Task<MediaInfo> ReadAsync(TestWorkspace workspace, string path)
    {
        var format = await ReadTextAsync(workspace, $"-v error -show_entries format=format_name,duration -of default=noprint_wrappers=1:nokey=0 \"{path}\"");
        var video = await ReadTextAsync(workspace, $"-v error -select_streams v:0 -show_entries stream=width,height,avg_frame_rate -of default=noprint_wrappers=1:nokey=0 \"{path}\"");
        var audio = await ReadTextAsync(workspace, $"-v error -select_streams a -show_entries stream=index -of csv=p=0 \"{path}\"");
        var subtitle = await ReadTextAsync(workspace, $"-v error -select_streams s -show_entries stream=index -of csv=p=0 \"{path}\"");

        var duration = 0d;
        var formatName = string.Empty;
        foreach (var line in format.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            if (parts[0] == "duration")
            {
                _ = double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out duration);
            }
            else if (parts[0] == "format_name")
            {
                formatName = parts[1];
            }
        }

        var width = 0;
        var height = 0;
        var fps = 0d;
        foreach (var line in video.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            switch (parts[0])
            {
                case "width":
                    _ = int.TryParse(parts[1], out width);
                    break;
                case "height":
                    _ = int.TryParse(parts[1], out height);
                    break;
                case "avg_frame_rate":
                    fps = ParseFraction(parts[1]);
                    break;
            }
        }

        return new MediaInfo(
            path,
            duration,
            width,
            height,
            fps,
            width > 0 && height > 0,
            !string.IsNullOrWhiteSpace(audio),
            !string.IsNullOrWhiteSpace(subtitle),
            formatName);
    }

    private static async Task<string> ReadTextAsync(TestWorkspace workspace, string arguments)
    {
        var (exitCode, output, error) = await workspace.Runner.RunFfprobeAsync(arguments);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe failed: {error}");
        }

        return output.Trim();
    }

    private static double ParseFraction(string value)
    {
        var parts = value.Split('/');
        if (parts.Length != 2)
        {
            return 0;
        }

        if (!double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var numerator) ||
            !double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var denominator) ||
            denominator == 0)
        {
            return 0;
        }

        return numerator / denominator;
    }
}
