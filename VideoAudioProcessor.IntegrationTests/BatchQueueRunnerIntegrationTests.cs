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
    public async Task BatchRunner_RunsCustomCommandJobs()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var request = workspace.Processing.BuildProcessingRequest(new Services.ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = media.VideoWithAudioPath,
            OutputFileName = "batch-custom",
            OutputFormat = "mp4",
            UseCustomCommand = true,
            CustomCommandTemplate = "-y -i \"{input}\" -t 1 -c:v libx264 -pix_fmt yuv420p -an \"{output}\""
        });

        var job = workspace.BatchRunner.CreateJob(request, "custom");
        await workspace.BatchRunner.RunJobAsync(job);

        Assert.That(job.Status, Is.EqualTo(BatchJobStatus.Completed));
        Assert.That(File.Exists(request.OutputPath), Is.True);

        var info = await MediaInfoReader.ReadAsync(workspace, request.OutputPath);
        Assert.That(info.HasVideo, Is.True);
        Assert.That(info.HasAudio, Is.False);
    }

    [Test]
    public async Task BatchRunner_RunsProjectRenderJobs_AndCleansTempFiles()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var project = new ProjectData
        {
            Name = "queued-project",
            Type = ProjectType.VideoCollage,
            Items =
            [
                new ProjectMediaItem { Path = media.VideoWithAudioPath, Kind = ProjectMediaKind.Video, DurationSeconds = 2 },
                new ProjectMediaItem { Path = media.ImageOnePath, Kind = ProjectMediaKind.Image, DurationSeconds = 1 }
            ],
            AudioItems =
            [
                new ProjectAudioItem { Path = media.AudioPath, DurationSeconds = 2 }
            ],
            SubtitleItems =
            [
                new ProjectSubtitleItem
                {
                    Text = "Queue subtitle",
                    StartSeconds = 0.2,
                    EndSeconds = 1.6,
                    FadeSeconds = 0.2,
                    FontSize = 40,
                    ColorHex = "#FFFFFF"
                }
            ],
            UseVideoAudio = false,
            OutputFormat = "mp4",
            Width = 320,
            Height = 240,
            Fps = 24
        };

        workspace.Storage.SaveProject(project);
        var request = workspace.ProjectRenderer.BuildRenderRequest(project, deleteProjectFileAfterRender: false);
        var tempFiles = request.TempFilesToDelete.ToList();
        var job = workspace.BatchRunner.CreateJob(request, "queued-project");

        await workspace.BatchRunner.RunJobAsync(job);

        Assert.That(job.Status, Is.EqualTo(BatchJobStatus.Completed));
        Assert.That(File.Exists(request.OutputPath), Is.True);
        Assert.That(tempFiles.All(path => !File.Exists(path)), Is.True);
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
