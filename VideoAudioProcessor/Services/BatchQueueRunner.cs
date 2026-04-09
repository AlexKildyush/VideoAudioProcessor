using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace VideoAudioProcessor.Services;

public sealed class BatchQueueRunner(FfmpegCommandRunner commandRunner)
{
    private readonly MediaProbeService _mediaProbe = new(commandRunner);

    public ProcessingJob CreateJob(ProcessingRequest request, string jobName)
    {
        return new ProcessingJob
        {
            Name = jobName,
            InputPaths = request.InputPaths.ToList(),
            OutputPath = request.OutputPath,
            Arguments = request.Arguments,
            Summary = request.Summary,
            ExpectedDurationSeconds = request.ExpectedDurationSeconds,
            IsMerge = request.IsMerge,
            IsProjectRender = request.IsProjectRender,
            TempFilesToDelete = request.TempFilesToDelete.ToList(),
            FilesToDeleteOnSuccess = request.FilesToDeleteOnSuccess.ToList(),
            Status = BatchJobStatus.Pending,
            StageText = "Ожидает"
        };
    }

    public async Task RunJobAsync(ProcessingJob job, CancellationToken cancellationToken = default)
    {
        if (job.Status == BatchJobStatus.Running)
        {
            return;
        }

        job.Status = BatchJobStatus.Running;
        job.LastError = null;
        job.StageText = "Подготовка";
        job.ProgressPercent = null;
        job.ProcessedDuration = TimeSpan.Zero;
        job.Elapsed = TimeSpan.Zero;
        job.EstimatedRemaining = null;
        job.CurrentSpeed = null;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (File.Exists(job.OutputPath))
            {
                throw new InvalidOperationException("Выходной файл уже существует.");
            }

            job.ExpectedDurationSeconds ??= await ResolveExpectedDurationSecondsAsync(job, cancellationToken);
            job.StageText = "Обработка";

            var progress = new Progress<FfmpegProgressInfo>(info =>
            {
                job.StageText = info.IsFinished ? "Завершение" : "Обработка";
                job.ProcessedDuration = info.ProcessedTime;
                job.Elapsed = stopwatch.Elapsed;
                job.CurrentSpeed = info.Speed;
                job.ProgressPercent = CalculateProgressPercent(info.ProcessedTime, job.ExpectedDurationSeconds);
                job.EstimatedRemaining = CalculateEstimatedRemaining(info.ProcessedTime, job.ExpectedDurationSeconds, info.Speed);
            });

            var (exitCode, errorOutput) = await commandRunner.RunFfmpegAsync(job.Arguments, cancellationToken, progress);
            if (exitCode != 0)
            {
                job.Status = BatchJobStatus.Failed;
                job.StageText = "Ошибка";
                job.LastError = errorOutput;
            }
            else
            {
                job.StageText = "Завершение";
                foreach (var path in job.FilesToDeleteOnSuccess.Where(File.Exists))
                {
                    File.Delete(path);
                }

                job.Status = BatchJobStatus.Completed;
                job.StageText = "Завершено";
                job.Elapsed = stopwatch.Elapsed;
                job.ProcessedDuration = job.ExpectedDurationSeconds.HasValue
                    ? TimeSpan.FromSeconds(job.ExpectedDurationSeconds.Value)
                    : stopwatch.Elapsed;
                job.ProgressPercent = 100;
                job.EstimatedRemaining = TimeSpan.Zero;
            }
        }
        catch (Exception ex)
        {
            job.Status = BatchJobStatus.Failed;
            job.StageText = "Ошибка";
            job.LastError = ex.Message;
            job.Elapsed = stopwatch.Elapsed;
        }
        finally
        {
            foreach (var path in job.TempFilesToDelete.Where(File.Exists))
            {
                File.Delete(path);
            }
        }
    }

    public async Task RunAllAsync(IEnumerable<ProcessingJob> jobs, CancellationToken cancellationToken = default)
    {
        foreach (var job in jobs.Where(j => j.Status is BatchJobStatus.Pending or BatchJobStatus.Failed).ToList())
        {
            await RunJobAsync(job, cancellationToken);
        }
    }

    internal static double? CalculateProgressPercent(TimeSpan processedTime, double? expectedDurationSeconds)
    {
        if (!expectedDurationSeconds.HasValue || expectedDurationSeconds.Value <= 0)
        {
            return null;
        }

        var percent = processedTime.TotalSeconds / expectedDurationSeconds.Value * 100d;
        return Math.Clamp(percent, 0, 100);
    }

    internal static TimeSpan? CalculateEstimatedRemaining(TimeSpan processedTime, double? expectedDurationSeconds, string? speedText)
    {
        if (!expectedDurationSeconds.HasValue || expectedDurationSeconds.Value <= 0)
        {
            return null;
        }

        var remainingSeconds = expectedDurationSeconds.Value - processedTime.TotalSeconds;
        if (remainingSeconds <= 0)
        {
            return TimeSpan.Zero;
        }

        var speed = ParseSpeed(speedText);
        if (!speed.HasValue || speed.Value <= 0)
        {
            return null;
        }

        return TimeSpan.FromSeconds(remainingSeconds / speed.Value);
    }

    private async Task<double?> ResolveExpectedDurationSecondsAsync(ProcessingJob job, CancellationToken cancellationToken)
    {
        if (job.ExpectedDurationSeconds.HasValue && job.ExpectedDurationSeconds.Value > 0)
        {
            return job.ExpectedDurationSeconds.Value;
        }

        if (job.IsMerge && job.InputPaths.Count > 0)
        {
            var total = 0d;
            foreach (var path in job.InputPaths.Where(File.Exists))
            {
                total += await _mediaProbe.GetMediaDurationAsync(path, cancellationToken);
            }

            return total > 0 ? total : null;
        }

        if (job.InputPaths.Count == 1 && File.Exists(job.InputPaths[0]))
        {
            return await _mediaProbe.GetMediaDurationAsync(job.InputPaths[0], cancellationToken);
        }

        return null;
    }

    private static double? ParseSpeed(string? speedText)
    {
        if (string.IsNullOrWhiteSpace(speedText))
        {
            return null;
        }

        var normalized = speedText.Trim().TrimEnd('x', 'X');
        return double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var speed) ? speed : null;
    }
}
