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
    public async Task TrackStorage_PersistsProject_WithVideoImageAudio_AndSubtitleTimeline()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var project = new ProjectData
        {
            Name = "full-project",
            Type = ProjectType.VideoCollage,
            Items =
            [
                new ProjectMediaItem { Path = media.VideoWithAudioPath, DurationSeconds = 2, Kind = ProjectMediaKind.Video },
                new ProjectMediaItem { Path = media.ImageOnePath, DurationSeconds = 1.5, Kind = ProjectMediaKind.Image }
            ],
            AudioItems =
            [
                new ProjectAudioItem { Path = media.AudioPath, DurationSeconds = 2.5 }
            ],
            SubtitleItems =
            [
                new ProjectSubtitleItem
                {
                    Text = "First subtitle",
                    StartSeconds = 0.1,
                    EndSeconds = 1.2,
                    FadeSeconds = 0.2,
                    FontSize = 40,
                    ColorHex = "#FFFFFF"
                },
                new ProjectSubtitleItem
                {
                    Text = "Second subtitle",
                    StartSeconds = 1.3,
                    EndSeconds = 2.6,
                    FadeSeconds = 0.3,
                    FontSize = 44,
                    ColorHex = "#FFD54F"
                }
            ],
            UseVideoAudio = false,
            OutputFormat = "mp4",
            Width = 1280,
            Height = 720
        };

        workspace.Storage.SaveProject(project);

        var loaded = workspace.Storage.LoadProject(ProjectType.VideoCollage, "full-project");
        workspace.ProjectRenderer.EnsureAudioItemsMigrated(loaded);

        Assert.That(loaded.Items, Has.Count.EqualTo(2));
        Assert.That(loaded.Items.Any(item => item.Kind == ProjectMediaKind.Video), Is.True);
        Assert.That(loaded.Items.Any(item => item.Kind == ProjectMediaKind.Image), Is.True);
        Assert.That(loaded.AudioItems, Has.Count.EqualTo(1));
        Assert.That(loaded.AudioItems[0].Path, Is.EqualTo(media.AudioPath));
        Assert.That(loaded.SubtitleItems, Has.Count.EqualTo(2));
        Assert.That(loaded.SubtitleItems[0].Text, Is.EqualTo("First subtitle"));
        Assert.That(loaded.SubtitleItems[1].ColorHex, Is.EqualTo("#FFD54F"));
    }

    [Test]
    public async Task TrackStorage_LoadProject_InitializesEmptySubtitleCollection_WhenItWasMissing()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var project = new ProjectData
        {
            Name = "without-subtitles",
            Type = ProjectType.VideoCollage,
            Items =
            [
                new ProjectMediaItem { Path = media.VideoWithAudioPath, DurationSeconds = 2, Kind = ProjectMediaKind.Video }
            ],
            AudioItems =
            [
                new ProjectAudioItem { Path = media.AudioPath, DurationSeconds = 2 }
            ],
            SubtitleItems = null!,
            OutputFormat = "mp4"
        };

        workspace.Storage.SaveProject(project);

        var loaded = workspace.Storage.LoadProject(ProjectType.VideoCollage, "without-subtitles");
        workspace.ProjectRenderer.EnsureAudioItemsMigrated(loaded);

        Assert.That(loaded.SubtitleItems, Is.Not.Null);
        Assert.That(loaded.SubtitleItems, Is.Empty);
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
