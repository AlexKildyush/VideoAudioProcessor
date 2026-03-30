using NUnit.Framework;

namespace VideoAudioProcessor.IntegrationTests;

[TestFixture]
internal sealed class BatchQueueRunnerIntegrationTests
{
    [Test]
    public async Task BatchRunner_RunsPendingAndFailedJobs_AndTracksStatuses()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var goodRequest = workspace.Processing.BuildProcessingRequest(new Services.ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = media.VideoWithAudioPath,
            OutputFileName = "batch-good",
            OutputFormat = "mp4"
        });

        var failingJob = new ProcessingJob
        {
            Name = "broken",
            OutputPath = workspace.Storage.GetProcessedOutputPath("batch-bad", "mp4"),
            Arguments = "-y -i missing-file.mp4 output.mp4",
            Summary = "broken",
            Status = BatchJobStatus.Pending
        };

        var goodJob = workspace.BatchRunner.CreateJob(goodRequest, "good");
        await workspace.BatchRunner.RunAllAsync([goodJob, failingJob]);

        Assert.That(goodJob.Status, Is.EqualTo(BatchJobStatus.Completed));
        Assert.That(File.Exists(goodRequest.OutputPath), Is.True);
        Assert.That(failingJob.Status, Is.EqualTo(BatchJobStatus.Failed));
        Assert.That(string.IsNullOrWhiteSpace(failingJob.LastError), Is.False);

        failingJob.Arguments = goodRequest.Arguments.Replace("batch-good.mp4", "batch-fixed.mp4");
        await workspace.BatchRunner.RunJobAsync(failingJob);

        Assert.That(failingJob.Status, Is.EqualTo(BatchJobStatus.Completed));
        Assert.That(File.Exists(workspace.Storage.GetProcessedOutputPath("batch-fixed", "mp4")), Is.True);
    }

    [Test]
    public async Task LosslessMerge_MergesCompatibleFiles_AndRejectsSingleInput()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();
        workspace.Storage.EnsureProcessedDirectory();
        var outputPath = workspace.Storage.GetProcessedOutputPath("merged", "mp4");

        var singleInput = Assert.Throws<InvalidOperationException>(() =>
            workspace.Processing.BuildLosslessMergeRequest([media.VideoWithAudioPath], outputPath));
        Assert.That(singleInput?.Message, Does.Contain("минимум два"));

        var request = workspace.Processing.BuildLosslessMergeRequest([media.VideoWithAudioPath, media.MergeVideoPath], outputPath);
        await workspace.Processing.ExecuteProcessingAsync(request);

        var info = await MediaInfoReader.ReadAsync(workspace, outputPath);
        Assert.That(info.HasVideo, Is.True);
        Assert.That(info.HasAudio, Is.True);
        Assert.That(info.Duration, Is.InRange(3.5, 4.6));
    }

    [Test]
    public async Task BatchRunner_FailsWhenOutputAlreadyExists()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();
        var request = workspace.Processing.BuildProcessingRequest(new Services.ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = media.VideoWithAudioPath,
            OutputFileName = "existing-batch",
            OutputFormat = "mp4"
        });

        workspace.Storage.EnsureProcessedDirectory();
        await File.WriteAllTextAsync(request.OutputPath, "exists");
        var job = workspace.BatchRunner.CreateJob(request, "existing");

        await workspace.BatchRunner.RunJobAsync(job);

        Assert.That(job.Status, Is.EqualTo(BatchJobStatus.Failed));
        Assert.That(job.LastError ?? string.Empty, Does.Contain("существует"));
    }
}
