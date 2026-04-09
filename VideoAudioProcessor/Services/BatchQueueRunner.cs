using System.IO;

namespace VideoAudioProcessor.Services;

public sealed class BatchQueueRunner(FfmpegCommandRunner commandRunner)
{
    public ProcessingJob CreateJob(ProcessingRequest request, string jobName)
    {
        return new ProcessingJob
        {
            Name = jobName,
            InputPaths = request.InputPaths.ToList(),
            OutputPath = request.OutputPath,
            Arguments = request.Arguments,
            Summary = request.Summary,
            IsMerge = request.IsMerge,
            TempFilesToDelete = request.TempFilesToDelete.ToList(),
            FilesToDeleteOnSuccess = request.FilesToDeleteOnSuccess.ToList(),
            Status = BatchJobStatus.Pending
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

        try
        {
            if (File.Exists(job.OutputPath))
            {
                throw new InvalidOperationException("Выходной файл уже существует.");
            }

            var (exitCode, errorOutput) = await commandRunner.RunFfmpegAsync(job.Arguments, cancellationToken);
            if (exitCode != 0)
            {
                job.Status = BatchJobStatus.Failed;
                job.LastError = errorOutput;
            }
            else
            {
                foreach (var path in job.FilesToDeleteOnSuccess.Where(File.Exists))
                {
                    File.Delete(path);
                }

                job.Status = BatchJobStatus.Completed;
            }
        }
        catch (Exception ex)
        {
            job.Status = BatchJobStatus.Failed;
            job.LastError = ex.Message;
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
}
