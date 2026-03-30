using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace VideoAudioProcessor.Services;

public sealed class MediaProbeService(FfmpegCommandRunner commandRunner)
{
    public async Task<bool> HasAudioStreamAsync(string path, CancellationToken cancellationToken = default)
    {
        var (_, output, _) = await commandRunner.RunFfprobeAsync(
            $"-v error -select_streams a -show_entries stream=codec_type -of csv=p=0 \"{path}\"",
            cancellationToken);

        return !string.IsNullOrWhiteSpace(output);
    }

    public bool HasAudioStream(string path)
    {
        return HasAudioStreamAsync(path).GetAwaiter().GetResult();
    }

    public async Task<double> GetMediaDurationAsync(string path, CancellationToken cancellationToken = default)
    {
        var duration = await GetMediaDurationInternalAsync(path, cancellationToken);
        return duration > 0 ? duration : 1;
    }

    public double GetMediaDuration(string path)
    {
        return GetMediaDurationAsync(path).GetAwaiter().GetResult();
    }

    public async Task<double> GetTrimmedDurationAsync(string path, double maxDuration, CancellationToken cancellationToken = default)
    {
        var duration = await GetMediaDurationInternalAsync(path, cancellationToken);
        if (duration <= 0)
        {
            return Math.Max(1, maxDuration);
        }

        if (maxDuration > 0 && duration > maxDuration)
        {
            return maxDuration;
        }

        return duration;
    }

    public double GetTrimmedDuration(string path, double maxDuration)
    {
        return GetTrimmedDurationAsync(path, maxDuration).GetAwaiter().GetResult();
    }

    private async Task<double> GetMediaDurationInternalAsync(string path, CancellationToken cancellationToken)
    {
        var (_, output, _) = await commandRunner.RunFfprobeAsync(
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"",
            cancellationToken);

        return double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var duration)
            ? duration
            : 0;
    }
}
