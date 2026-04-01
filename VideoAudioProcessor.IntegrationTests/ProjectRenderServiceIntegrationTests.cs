using NUnit.Framework;

namespace VideoAudioProcessor.IntegrationTests;

[TestFixture]
internal sealed class ProjectRenderServiceIntegrationTests
{
    [Test]
    public async Task VideoCollage_Render_UsesVideoAudio_AndDeletesProjectFile()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var project = new ProjectData
        {
            Name = "collage-video-audio",
            Type = ProjectType.VideoCollage,
            Items =
            [
                new ProjectMediaItem { Path = media.VideoWithAudioPath, Kind = ProjectMediaKind.Video },
                new ProjectMediaItem { Path = media.VideoWithoutAudioPath, Kind = ProjectMediaKind.Video }
            ],
            UseVideoAudio = true,
            OutputFormat = "mp4",
            Width = 641,
            Height = 359,
            Fps = 25,
            MaxClipDurationSeconds = 1.2
        };

        workspace.Storage.SaveProject(project);
        workspace.ProjectRenderer.ValidateProjectForRender(project, project.OutputFormat);
        var outputPath = await workspace.ProjectRenderer.RenderProjectAsync(project);

        Assert.That(File.Exists(outputPath), Is.True);
        Assert.That(File.Exists(workspace.Storage.GetProjectFilePath(project.Type, project.Name)), Is.False);

        var info = await MediaInfoReader.ReadAsync(workspace, outputPath);
        Assert.That(info.HasVideo, Is.True);
        Assert.That(info.HasAudio, Is.True);
        Assert.That(info.Width, Is.EqualTo(640));
        Assert.That(info.Height, Is.EqualTo(358));
        Assert.That(info.Duration, Is.InRange(2.0, 3.0));
    }

    [Test]
    public async Task VideoCollage_Render_UsesExternalAudio_AndSilentFallback()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var externalAudioProject = new ProjectData
        {
            Name = "collage-external-audio",
            Type = ProjectType.VideoCollage,
            Items =
            [
                new ProjectMediaItem { Path = media.VideoWithoutAudioPath, Kind = ProjectMediaKind.Video, DurationSeconds = 2 }
            ],
            AudioItems = [new ProjectAudioItem { Path = media.AudioPath, DurationSeconds = 1.5 }],
            UseVideoAudio = false,
            OutputFormat = "mp4",
            Width = 320,
            Height = 240,
            Fps = 24
        };

        var silentProject = new ProjectData
        {
            Name = "collage-silent",
            Type = ProjectType.VideoCollage,
            Items =
            [
                new ProjectMediaItem { Path = media.VideoWithoutAudioPath, Kind = ProjectMediaKind.Video, DurationSeconds = 1.2 }
            ],
            UseVideoAudio = false,
            OutputFormat = "mp4",
            Width = 320,
            Height = 240,
            Fps = 24
        };

        var externalOutput = await workspace.ProjectRenderer.RenderProjectAsync(externalAudioProject, deleteProjectFileAfterRender: false);
        var silentOutput = await workspace.ProjectRenderer.RenderProjectAsync(silentProject, deleteProjectFileAfterRender: false);

        var externalInfo = await MediaInfoReader.ReadAsync(workspace, externalOutput);
        var silentInfo = await MediaInfoReader.ReadAsync(workspace, silentOutput);

        Assert.That(externalInfo.HasAudio, Is.True);
        Assert.That(silentInfo.HasAudio, Is.True);
    }

    [Test]
    public async Task SlideShow_Render_AssignsDefaultDurations_AndProducesVideo()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var slideShow = new ProjectData
        {
            Name = "slideshow",
            Type = ProjectType.SlideShow,
            Items =
            [
                new ProjectMediaItem { Path = media.ImageOnePath, Kind = ProjectMediaKind.Image, DurationSeconds = 0 },
                new ProjectMediaItem { Path = media.ImageTwoPath, Kind = ProjectMediaKind.Image, DurationSeconds = 0 }
            ],
            SlideDurationSeconds = 1.5,
            UseVideoAudio = false,
            OutputFormat = "mp4",
            Width = 320,
            Height = 240,
            Fps = 25
        };

        var outputPath = await workspace.ProjectRenderer.RenderProjectAsync(slideShow, deleteProjectFileAfterRender: false);
        var info = await MediaInfoReader.ReadAsync(workspace, outputPath);

        Assert.That(info.HasVideo, Is.True);
        Assert.That(info.HasAudio, Is.True);
        Assert.That(info.Duration, Is.InRange(2.5, 4.0));
        Assert.That(slideShow.Items.All(item => item.DurationSeconds >= 1.5), Is.True);
    }

    [Test]
    public async Task VideoCollage_Render_WithSubtitleTimeline_BuildsBurnedInSubtitles()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var project = new ProjectData
        {
            Name = "collage-subtitles",
            Type = ProjectType.VideoCollage,
            Items =
            [
                new ProjectMediaItem { Path = media.VideoWithAudioPath, Kind = ProjectMediaKind.Video, DurationSeconds = 2 }
            ],
            SubtitleItems =
            [
                new ProjectSubtitleItem
                {
                    Text = "Hello world",
                    StartSeconds = 0.2,
                    EndSeconds = 1.4,
                    FadeSeconds = 0.25,
                    FontSize = 38,
                    ColorHex = "#FFD54F"
                }
            ],
            UseVideoAudio = true,
            OutputFormat = "mp4",
            Width = 320,
            Height = 240,
            Fps = 24
        };

        var expectedOutputPath = workspace.Storage.GetProcessedOutputPath(project.Name, project.OutputFormat);
        var (arguments, tempFiles) = workspace.ProjectRenderer.BuildVideoCollageArguments(project, expectedOutputPath);

        Assert.That(arguments, Does.Contain("subtitles="));
        Assert.That(tempFiles.Count, Is.EqualTo(1));
        Assert.That(File.Exists(tempFiles[0]), Is.True);

        var outputPath = await workspace.ProjectRenderer.RenderProjectAsync(project, deleteProjectFileAfterRender: false);
        var info = await MediaInfoReader.ReadAsync(workspace, outputPath);

        Assert.That(info.HasVideo, Is.True);
        Assert.That(info.HasAudio, Is.True);
    }

    [Test]
    public async Task ProjectRender_ValidationRejectsMissingInputs_AndExistingOutput()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var emptyProject = new ProjectData { Name = "empty", Type = ProjectType.VideoCollage, OutputFormat = "mp4" };
        Assert.Throws<InvalidOperationException>(() => workspace.ProjectRenderer.ValidateProjectForRender(emptyProject, "mp4"));

        var missingFileProject = new ProjectData
        {
            Name = "missing-file",
            Type = ProjectType.VideoCollage,
            Items = [new ProjectMediaItem { Path = Path.Combine(workspace.RootPath, "missing.mp4"), Kind = ProjectMediaKind.Video }],
            OutputFormat = "mp4"
        };
        Assert.Throws<InvalidOperationException>(() => workspace.ProjectRenderer.ValidateProjectForRender(missingFileProject, "mp4"));

        var project = new ProjectData
        {
            Name = "existing-output",
            Type = ProjectType.VideoCollage,
            Items = [new ProjectMediaItem { Path = media.VideoWithAudioPath, Kind = ProjectMediaKind.Video }],
            OutputFormat = "mp4"
        };
        workspace.Storage.EnsureProcessedDirectory();
        await File.WriteAllTextAsync(workspace.Storage.GetProcessedOutputPath(project.Name, "mp4"), "busy");
        Assert.Throws<InvalidOperationException>(() => workspace.ProjectRenderer.ValidateProjectForRender(project, "mp4"));
    }
}
