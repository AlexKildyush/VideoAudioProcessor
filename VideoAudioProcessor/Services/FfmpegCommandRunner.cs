using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VideoAudioProcessor.Services;

public sealed class FfmpegCommandRunner
{
    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        using var process = CreateProcess(fileName, arguments);
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await standardOutputTask, await standardErrorTask);
    }

    public async Task<(int ExitCode, string ErrorOutput)> RunFfmpegAsync(
        string arguments,
        CancellationToken cancellationToken = default,
        IProgress<FfmpegProgressInfo>? progress = null)
    {
        var progressArguments = $"-progress pipe:1 -nostats {arguments}";
        using var process = CreateProcess("ffmpeg", progressArguments);
        process.Start();

        var errorBuilder = new StringBuilder();
        var standardErrorTask = ReadStandardErrorAsync(process, errorBuilder, cancellationToken);
        var standardOutputTask = ReadProgressAsync(process, progress, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(standardOutputTask, standardErrorTask);

        return (process.ExitCode, errorBuilder.ToString());
    }

    public Task<(int ExitCode, string StandardOutput, string StandardError)> RunFfprobeAsync(string arguments, CancellationToken cancellationToken = default)
    {
        return RunProcessAsync("ffprobe", arguments, cancellationToken);
    }

    internal static FfmpegProgressInfo? ParseProgressState(IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue("progress", out var rawProgressState))
        {
            return null;
        }

        var processedDuration = TimeSpan.Zero;
        if (values.TryGetValue("out_time_ms", out var outTimeMs) &&
            long.TryParse(outTimeMs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
        {
            processedDuration = TimeSpan.FromMilliseconds(microseconds / 1000d);
        }
        else if (values.TryGetValue("out_time_us", out var outTimeUs) &&
                 long.TryParse(outTimeUs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var altMicroseconds))
        {
            processedDuration = TimeSpan.FromMilliseconds(altMicroseconds / 1000d);
        }
        else if (values.TryGetValue("out_time", out var outTime) &&
                 TimeSpan.TryParse(outTime, CultureInfo.InvariantCulture, out var parsedTime))
        {
            processedDuration = parsedTime;
        }

        values.TryGetValue("speed", out var speed);
        return new FfmpegProgressInfo(processedDuration, NormalizeSpeed(speed), rawProgressState == "end", rawProgressState);
    }

    private static Process CreateProcess(string fileName, string arguments)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
    }

    private static async Task ReadStandardErrorAsync(Process process, StringBuilder errorBuilder, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                return;
            }

            errorBuilder.AppendLine(line);
        }
    }

    private static async Task ReadProgressAsync(Process process, IProgress<FfmpegProgressInfo>? progress, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex];
            var value = line[(separatorIndex + 1)..];
            values[key] = value;

            if (!string.Equals(key, "progress", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var snapshot = ParseProgressState(values);
            if (snapshot != null)
            {
                progress?.Report(snapshot);
            }

            values.Clear();
        }
    }

    private static string? NormalizeSpeed(string? speed)
    {
        if (string.IsNullOrWhiteSpace(speed))
        {
            return null;
        }

        return speed.Trim();
    }
}

public sealed record FfmpegProgressInfo(
    TimeSpan ProcessedTime,
    string? Speed,
    bool IsFinished,
    string RawProgressState);
