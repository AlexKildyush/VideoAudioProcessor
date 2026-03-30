using NUnit.Framework;

namespace VideoAudioProcessor.IntegrationTests;

[TestFixture]
internal sealed class TrackStorageAndProjectIntegrationTests
{
    [Test]
    public async Task TrackStorage_HandlesQueueCopyUniqueNames_AndSupportedFiles()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var firstCopy = workspace.Storage.CopyToQueue(media.VideoWithAudioPath);
        var secondCopy = workspace.Storage.CopyToQueue(media.VideoWithAudioPath);

        Assert.That(File.Exists(firstCopy), Is.True);
        Assert.That(File.Exists(secondCopy), Is.True);
        Assert.That(firstCopy, Is.Not.EqualTo(secondCopy));
        Assert.That(workspace.Storage.GetSupportedQueueFiles(), Does.Contain(Path.GetFileName(firstCopy)));
        Assert.That(workspace.Storage.GetSupportedQueueFiles(), Does.Contain(Path.GetFileName(secondCopy)));
    }

    [Test]
    public async Task TrackStorage_PersistsProjects_AndSupportsRenameDeleteAndMigration()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var project = new ProjectData
        {
            Name = "demo",
            Type = ProjectType.VideoCollage,
            Items =
            [
                new ProjectMediaItem { Path = media.VideoWithAudioPath, DurationSeconds = 2, Kind = ProjectMediaKind.Video }
            ],
            AudioPath = media.AudioPath,
            AudioDurationSeconds = 3,
            OutputFormat = "mp4"
        };

        workspace.Storage.SaveProject(project);
        Assert.That(workspace.Storage.ListProjectNames(ProjectType.VideoCollage), Does.Contain("demo"));

        var loaded = workspace.Storage.LoadProject(ProjectType.VideoCollage, "demo");
        workspace.ProjectRenderer.EnsureAudioItemsMigrated(loaded);
        Assert.That(loaded.AudioItems, Has.Count.EqualTo(1));
        Assert.That(loaded.AudioItems[0].Path, Is.EqualTo(media.AudioPath));

        workspace.Storage.RenameProject(ProjectType.VideoCollage, "demo", "renamed");
        Assert.That(workspace.Storage.ListProjectNames(ProjectType.VideoCollage), Does.Contain("renamed"));

        workspace.Storage.DeleteProject(ProjectType.VideoCollage, "renamed");
        Assert.That(workspace.Storage.ListProjectNames(ProjectType.VideoCollage), Does.Not.Contain("renamed"));
    }

    [Test]
    public async Task TrackStorage_CollectsBaseTracks_FromQueueAndProcessed()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        workspace.Storage.CopyToQueue(media.VideoWithAudioPath);
        var processedRequest = workspace.Processing.BuildProcessingRequest(new VideoAudioProcessor.Services.ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = media.VideoWithAudioPath,
            OutputFileName = "processed-base",
            OutputFormat = "mp4"
        });
        await workspace.Processing.ExecuteProcessingAsync(processedRequest);

        var tracks = workspace.Storage.GetBaseTracks(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase));
        Assert.That(tracks.Any(item => item.Source == "В очереди"), Is.True);
        Assert.That(tracks.Any(item => item.Source == "Обработанные"), Is.True);
    }
}
