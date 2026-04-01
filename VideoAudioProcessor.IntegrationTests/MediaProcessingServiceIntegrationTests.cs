using NUnit.Framework;
using VideoAudioProcessor.Services;

namespace VideoAudioProcessor.IntegrationTests;

[TestFixture]
internal sealed class MediaProcessingServiceIntegrationTests
{
    [Test]
    public async Task Processing_EndToEndScenarios_CreateExpectedOutputs()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var scenarios = new[]
        {
            new ProcessingOptions { RootPath = workspace.RootPath, InputPath = media.VideoWithAudioPath, OutputFileName = "transcoded", OutputFormat = "mp4", OutputWidth = 640, OutputHeight = 360 },
            new ProcessingOptions { RootPath = workspace.RootPath, InputPath = media.VideoWithAudioPath, OutputFileName = "trimmed", OutputFormat = "mkv", TrimStart = "00:00:00.2", TrimEnd = "00:00:01.2" },
            new ProcessingOptions { RootPath = workspace.RootPath, InputPath = media.VideoWithAudioPath, OutputFileName = "cropped", OutputFormat = "avi", CropResizeEnabled = true, CropValue = "200:200:0:0", ScaleValue = "160:160" },
            new ProcessingOptions { RootPath = workspace.RootPath, InputPath = media.VideoWithAudioPath, OutputFileName = "fpschange", OutputFormat = "mp4", FpsChangeEnabled = true, FpsValue = "15" },
            new ProcessingOptions { RootPath = workspace.RootPath, InputPath = media.VideoWithAudioPath, OutputFileName = "vp9", OutputFormat = "mkv", Vp9Enabled = true, Vp9CrfValue = "35" },
            new ProcessingOptions { RootPath = workspace.RootPath, InputPath = media.VideoWithAudioPath, OutputFileName = "fast", OutputFormat = "mp4", FastPresetEnabled = true },
            new ProcessingOptions { RootPath = workspace.RootPath, InputPath = media.VideoWithAudioPath, OutputFileName = "noaudio", OutputFormat = "mp4", RemoveAudio = true },
            new ProcessingOptions { RootPath = workspace.RootPath, InputPath = media.VideoWithAudioPath, OutputFileName = "audioonly", OutputFormat = "mkv", ExtractOpus = true },
            new ProcessingOptions { RootPath = workspace.RootPath, InputPath = media.VideoWithAudioPath, OutputFileName = "lossless", OutputFormat = "mp4", LosslessCopy = true },
            new ProcessingOptions { RootPath = workspace.RootPath, InputPath = media.VideoWithAudioPath, OutputFileName = "burnin", OutputFormat = "mp4", SubtitleMode = SubtitleMode.BurnIn, SubtitlePath = media.SubtitlePath },
            new ProcessingOptions { RootPath = workspace.RootPath, InputPath = media.VideoWithAudioPath, OutputFileName = "embedded", OutputFormat = "mkv", SubtitleMode = SubtitleMode.Embed, SubtitlePath = media.SubtitlePath }
        };

        foreach (var options in scenarios)
        {
            var request = workspace.Processing.BuildProcessingRequest(options);
            await workspace.Processing.ExecuteProcessingAsync(request);
            Assert.That(File.Exists(request.OutputPath), Is.True, request.OutputPath);
        }

        var transcoded = await MediaInfoReader.ReadAsync(workspace, workspace.Storage.GetProcessedOutputPath("transcoded", "mp4"));
        Assert.That(transcoded.HasVideo, Is.True);
        Assert.That(transcoded.HasAudio, Is.True);
        Assert.That(transcoded.Width, Is.EqualTo(640));
        Assert.That(transcoded.Height, Is.EqualTo(360));

        var trimmed = await MediaInfoReader.ReadAsync(workspace, workspace.Storage.GetProcessedOutputPath("trimmed", "mkv"));
        Assert.That(trimmed.Duration, Is.InRange(0.7, 1.4));

        var cropped = await MediaInfoReader.ReadAsync(workspace, workspace.Storage.GetProcessedOutputPath("cropped", "avi"));
        Assert.That(cropped.Width, Is.EqualTo(160));
        Assert.That(cropped.Height, Is.EqualTo(160));

        var fpsChanged = await MediaInfoReader.ReadAsync(workspace, workspace.Storage.GetProcessedOutputPath("fpschange", "mp4"));
        Assert.That(fpsChanged.Fps, Is.InRange(14, 16));

        var noAudio = await MediaInfoReader.ReadAsync(workspace, workspace.Storage.GetProcessedOutputPath("noaudio", "mp4"));
        Assert.That(noAudio.HasAudio, Is.False);

        var audioOnly = await MediaInfoReader.ReadAsync(workspace, workspace.Storage.GetProcessedOutputPath("audioonly", "mkv"));
        Assert.That(audioOnly.HasVideo, Is.False);
        Assert.That(audioOnly.HasAudio, Is.True);

        var embedded = await MediaInfoReader.ReadAsync(workspace, workspace.Storage.GetProcessedOutputPath("embedded", "mkv"));
        Assert.That(embedded.HasSubtitle, Is.True);
    }

    [Test]
    public async Task CustomCommand_UsesTemplates_AndProducesOutput()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();
        workspace.Storage.EnsureProcessedDirectory();
        var outputPath = workspace.Storage.GetProcessedOutputPath("custom", "mp4");

        var command = workspace.Processing.BuildCustomCommand(
            "-y -i \"{input}\" -t 1 -c:v libx264 -pix_fmt yuv420p -an \"{output}\"",
            media.VideoWithAudioPath,
            outputPath);

        await workspace.Processing.ExecuteCustomCommandAsync(command);

        var info = await MediaInfoReader.ReadAsync(workspace, outputPath);
        Assert.That(info.HasVideo, Is.True);
        Assert.That(info.HasAudio, Is.False);
        Assert.That(info.Duration, Is.InRange(0.7, 1.3));
    }

    [Test]
    public async Task BuildProcessingRequest_WithCustomCommand_CreatesExecutableRequest()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var request = workspace.Processing.BuildProcessingRequest(new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = media.VideoWithAudioPath,
            OutputFileName = "custom-request",
            OutputFormat = "mp4",
            UseCustomCommand = true,
            CustomCommandTemplate = "-y -i \"{input}\" -t 1 -c:v libx264 -pix_fmt yuv420p -an \"{output}\""
        });

        Assert.That(request.Summary, Does.Contain("Custom ffmpeg"));
        Assert.That(request.Arguments, Does.Contain(media.VideoWithAudioPath));
        Assert.That(request.Arguments, Does.Contain(request.OutputPath));

        await workspace.Processing.ExecuteProcessingAsync(request);

        var info = await MediaInfoReader.ReadAsync(workspace, request.OutputPath);
        Assert.That(info.HasVideo, Is.True);
        Assert.That(info.HasAudio, Is.False);
        Assert.That(info.Duration, Is.InRange(0.7, 1.3));
    }

    [Test]
    public async Task BuildProcessingRequest_ValidatesMissingInputAndConflicts()
    {
        await using var workspace = new TestWorkspace();
        var media = await workspace.CreateMediaSetAsync();

        var missingInput = Assert.Throws<InvalidOperationException>(() => workspace.Processing.BuildProcessingRequest(new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = string.Empty,
            OutputFileName = "x",
            OutputFormat = "mp4"
        }));
        Assert.That(missingInput?.Message, Does.Contain("Сначала выберите файл"));

        var missingFile = Assert.Throws<InvalidOperationException>(() => workspace.Processing.BuildProcessingRequest(new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = Path.Combine(workspace.RootPath, "missing.mp4"),
            OutputFileName = "x",
            OutputFormat = "mp4"
        }));
        Assert.That(missingFile?.Message, Does.Contain("не найден"));

        workspace.Storage.EnsureProcessedDirectory();
        await File.WriteAllTextAsync(workspace.Storage.GetProcessedOutputPath("existing", "mp4"), "busy");
        var existing = Assert.Throws<InvalidOperationException>(() => workspace.Processing.BuildProcessingRequest(new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = media.VideoWithAudioPath,
            OutputFileName = "existing",
            OutputFormat = "mp4"
        }));
        Assert.That(existing?.Message, Does.Contain("уже существует"));

        var lossless = Assert.Throws<InvalidOperationException>(() => workspace.Processing.BuildProcessingRequest(new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = media.VideoWithAudioPath,
            OutputFileName = "bad-lossless",
            OutputFormat = "mp4",
            LosslessCopy = true,
            FpsChangeEnabled = true,
            FpsValue = "15"
        }));
        Assert.That(lossless?.Message, Does.Contain("Lossless"));

        var extract = Assert.Throws<InvalidOperationException>(() => workspace.Processing.BuildProcessingRequest(new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = media.VideoWithAudioPath,
            OutputFileName = "bad-audio",
            OutputFormat = "mp4",
            ExtractOpus = true,
            SubtitleMode = SubtitleMode.BurnIn,
            SubtitlePath = media.SubtitlePath
        }));
        Assert.That(extract?.Message, Does.Contain("аудио-only"));

        var embedAvi = Assert.Throws<InvalidOperationException>(() => workspace.Processing.BuildProcessingRequest(new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = media.VideoWithAudioPath,
            OutputFileName = "bad-avi",
            OutputFormat = "avi",
            SubtitleMode = SubtitleMode.Embed,
            SubtitlePath = media.SubtitlePath
        }));
        Assert.That(embedAvi?.Message, Does.Contain("AVI"));

        var customWithoutInputPlaceholder = Assert.Throws<InvalidOperationException>(() => workspace.Processing.BuildProcessingRequest(new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = media.VideoWithAudioPath,
            OutputFileName = "bad-custom-input",
            OutputFormat = "mp4",
            UseCustomCommand = true,
            CustomCommandTemplate = "-y -c:v libx264 \"{output}\""
        }));
        Assert.That(customWithoutInputPlaceholder?.Message, Does.Contain("{input}"));

        var customWithoutOutputPlaceholder = Assert.Throws<InvalidOperationException>(() => workspace.Processing.BuildProcessingRequest(new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = media.VideoWithAudioPath,
            OutputFileName = "bad-custom-output",
            OutputFormat = "mp4",
            UseCustomCommand = true,
            CustomCommandTemplate = "-y -i \"{input}\" -c:v libx264"
        }));
        Assert.That(customWithoutOutputPlaceholder?.Message, Does.Contain("{output}"));
    }

    [Test]
    public async Task BuildStandardArguments_ContainsHardwareFlags_ForGpuModes()
    {
        await using var workspace = new TestWorkspace();

        var autoArgs = workspace.Processing.BuildStandardArguments("input.mp4", "output.mp4", new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = "input.mp4",
            OutputFileName = "gpu",
            OutputFormat = "mp4",
            HardwareDecodeEnabled = true,
            HardwareAccelerationMode = HardwareAccelerationMode.Auto
        });
        var nvencArgs = workspace.Processing.BuildStandardArguments("input.mp4", "output.mp4", new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = "input.mp4",
            OutputFileName = "gpu",
            OutputFormat = "mp4",
            HardwareDecodeEnabled = true,
            HardwareAccelerationMode = HardwareAccelerationMode.NvidiaNvenc
        });
        var qsvArgs = workspace.Processing.BuildStandardArguments("input.mp4", "output.mp4", new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = "input.mp4",
            OutputFileName = "gpu",
            OutputFormat = "mp4",
            HardwareDecodeEnabled = true,
            HardwareAccelerationMode = HardwareAccelerationMode.IntelQsv
        });
        var amfArgs = workspace.Processing.BuildStandardArguments("input.mp4", "output.mp4", new ProcessingOptions
        {
            RootPath = workspace.RootPath,
            InputPath = "input.mp4",
            OutputFileName = "gpu",
            OutputFormat = "mp4",
            HardwareDecodeEnabled = true,
            HardwareAccelerationMode = HardwareAccelerationMode.AmdAmf
        });

        Assert.That(autoArgs, Does.Contain("-hwaccel auto"));
        Assert.That(nvencArgs, Does.Contain("-hwaccel cuda"));
        Assert.That(nvencArgs, Does.Contain("h264_nvenc"));
        Assert.That(qsvArgs, Does.Contain("-hwaccel qsv"));
        Assert.That(qsvArgs, Does.Contain("h264_qsv"));
        Assert.That(amfArgs, Does.Contain("-hwaccel d3d11va"));
        Assert.That(amfArgs, Does.Contain("h264_amf"));
    }
}
