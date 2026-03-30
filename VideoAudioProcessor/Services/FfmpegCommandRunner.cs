using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace VideoAudioProcessor.Services;

public sealed class FfmpegCommandRunner
{
    public async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        using var process = new Process
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

        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await standardOutputTask, await standardErrorTask);
    }

    public async Task<(int ExitCode, string ErrorOutput)> RunFfmpegAsync(string arguments, CancellationToken cancellationToken = default)
    {
        var (exitCode, _, standardError) = await RunProcessAsync("ffmpeg", arguments, cancellationToken);
        return (exitCode, standardError);
    }

    public Task<(int ExitCode, string StandardOutput, string StandardError)> RunFfprobeAsync(string arguments, CancellationToken cancellationToken = default)
    {
        return RunProcessAsync("ffprobe", arguments, cancellationToken);
    }
}
